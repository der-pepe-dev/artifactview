using System.Text;

namespace ArtifactView.Infrastructure.Sources.DiskImage;

// Raw exFAT scanner for DELETED files. DiscUtils has no exFAT support at all, so this is a
// from-scratch parser of the exFAT directory entry-set model. A directory entry's type byte
// has its high bit (0x80, "InUse") cleared when deleted: File 0x85->0x05, Stream-Extension
// 0xC0->0x40, File-Name 0xC1->0x41. Recovery is best-effort and assumes contiguous storage
// (the cluster chain is gone after delete), like the FAT path.
public static class ExFatScanner
{
    public sealed record ExFatDeletedEntry(string Name, long FirstCluster, long Size);

    public sealed record ExFatGeometry(
        long ClusterSize, long FatByteOffset, long FatSizeBytes,
        long ClusterHeapByteOffset, long RootCluster);

    public static ExFatGeometry? ReadGeometry(System.IO.Stream vol)
    {
        try
        {
            vol.Seek(0, System.IO.SeekOrigin.Begin);
            var boot = new byte[512];
            if (ReadFully(vol, boot, 0, boot.Length) != boot.Length) return null;

            // FileSystemName "EXFAT   " at offset 3 (8 bytes).
            if (Encoding.ASCII.GetString(boot, 3, 8) != "EXFAT   ") return null;

            int bps = 1 << boot[0x6C]; // BytesPerSectorShift
            int spc = 1 << boot[0x6D]; // SectorsPerClusterShift
            if (bps == 0 || spc == 0) return null;

            long fatOffset         = U32(boot, 0x50);
            long fatLength         = U32(boot, 0x54);
            long clusterHeapOffset = U32(boot, 0x58);
            long rootCluster       = U32(boot, 0x60);

            return new ExFatGeometry(
                (long)bps * spc,
                fatOffset * bps,
                fatLength * bps,
                clusterHeapOffset * bps,
                rootCluster);
        }
        catch { return null; }
    }

    public static long ClusterByteOffset(ExFatGeometry g, long cluster)
        => g.ClusterHeapByteOffset + (cluster - 2) * g.ClusterSize;

    public static IReadOnlyList<ExFatDeletedEntry> Scan(System.IO.Stream vol, ExFatGeometry g, ISet<string> mediaExtensions)
    {
        var results = new List<ExFatDeletedEntry>();
        var fat = ReadFat(vol, g);
        var visited = new HashSet<long>();
        var rootBytes = ReadClusterChain(vol, g, fat, g.RootCluster, visited);
        ScanDirectory(vol, g, fat, rootBytes, mediaExtensions, results, visited, depth: 0);
        return results;
    }

    public static byte[]? RecoverContiguous(System.IO.Stream vol, ExFatGeometry g, long firstCluster, long size, long maxBytes)
    {
        if (firstCluster < 2 || size <= 0 || size > maxBytes) return null;
        var buf = new byte[(int)size];
        vol.Seek(ClusterByteOffset(g, firstCluster), System.IO.SeekOrigin.Begin);
        return ReadFully(vol, buf, 0, buf.Length) == buf.Length ? buf : null;
    }

    private static void ScanDirectory(
        System.IO.Stream vol, ExFatGeometry g, uint[] fat, byte[] dir,
        ISet<string> mediaExtensions, List<ExFatDeletedEntry> results, HashSet<long> visited, int depth)
    {
        if (depth > 64) return;

        int o = 0;
        while (o + 32 <= dir.Length)
        {
            byte type = dir[o];
            if (type == 0x00) break;          // end of directory
            int typeCode = type & 0x7F;

            if (typeCode != 0x05)             // not a File directory entry
            {
                o += 32;
                continue;
            }

            bool deleted   = (type & 0x80) == 0;
            int secondary  = dir[o + 1];      // SecondaryCount (Stream + Name entries)
            ushort attrs   = (ushort)U16(dir, o + 4);
            bool isDir     = (attrs & 0x10) != 0;

            int streamOff = o + 32;
            int setEntries = 1 + secondary;
            o += setEntries * 32;             // advance past the whole entry set

            if (streamOff + 32 > dir.Length) continue;
            if ((dir[streamOff] & 0x7F) != 0x40) continue; // expected Stream-Extension

            int nameLen        = dir[streamOff + 3];
            long firstCluster  = U32(dir, streamOff + 20);
            long dataLength     = (long)U64(dir, streamOff + 24);
            string name        = ReadName(dir, streamOff + 32, nameLen, secondary - 1);

            if (isDir)
            {
                if (!deleted && firstCluster >= 2)
                {
                    var child = ReadClusterChain(vol, g, fat, firstCluster, visited);
                    if (child.Length > 0)
                        ScanDirectory(vol, g, fat, child, mediaExtensions, results, visited, depth + 1);
                }
                continue;
            }

            if (!deleted) continue;           // live files are out of scope for v1
            var ext = System.IO.Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext) || !mediaExtensions.Contains(ext)) continue;
            if (firstCluster < 2 || dataLength <= 0) continue;

            results.Add(new ExFatDeletedEntry(name, firstCluster, dataLength));
        }
    }

    // Reads the file name from the File-Name entries following the Stream entry.
    // Each File-Name entry holds 15 UTF-16LE chars at offset +2.
    private static string ReadName(byte[] dir, int firstNameOff, int nameLen, int nameEntryCount)
    {
        var sb = new StringBuilder(nameLen);
        int remaining = nameLen;
        for (int e = 0; e < nameEntryCount && remaining > 0; e++)
        {
            int entry = firstNameOff + e * 32;
            if (entry + 32 > dir.Length) break;
            if ((dir[entry] & 0x7F) != 0x41) break; // not a File-Name entry
            int take = Math.Min(15, remaining);
            for (int i = 0; i < take; i++)
            {
                char c = (char)U16(dir, entry + 2 + i * 2);
                sb.Append(c);
            }
            remaining -= take;
        }
        return sb.ToString();
    }

    private static byte[] ReadClusterChain(System.IO.Stream vol, ExFatGeometry g, uint[] fat, long startCluster, HashSet<long> visited)
    {
        var ms = new System.IO.MemoryStream();
        long cluster = startCluster;
        int guard = 0;
        while (cluster >= 2 && cluster < fat.LongLength && guard++ < 1 << 20)
        {
            if (!visited.Add(cluster)) break;
            var buf = new byte[g.ClusterSize];
            vol.Seek(ClusterByteOffset(g, cluster), System.IO.SeekOrigin.Begin);
            if (ReadFully(vol, buf, 0, buf.Length) != buf.Length) break;
            ms.Write(buf, 0, buf.Length);

            uint next = fat[cluster];
            if (next >= 0xFFFFFFF8u || next < 2) break;
            cluster = next;
        }
        return ms.ToArray();
    }

    private static uint[] ReadFat(System.IO.Stream vol, ExFatGeometry g)
    {
        int size = (int)Math.Min(g.FatSizeBytes, 1 << 26);
        var raw = new byte[size];
        vol.Seek(g.FatByteOffset, System.IO.SeekOrigin.Begin);
        ReadFully(vol, raw, 0, raw.Length);

        var fat = new uint[size / 4];
        for (int i = 0; i < fat.Length; i++) fat[i] = U32(raw, i * 4);
        return fat;
    }

    private static int   U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    private static uint  U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static ulong U64(byte[] b, int o)
    {
        ulong v = 0;
        for (int i = 7; i >= 0; i--) v = (v << 8) | b[o + i];
        return v;
    }

    private static int ReadFully(System.IO.Stream s, byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, offset + total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
