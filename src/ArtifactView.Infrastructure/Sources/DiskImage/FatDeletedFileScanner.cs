namespace ArtifactView.Infrastructure.Sources.DiskImage;

// Raw FAT12/16/32 scanner for DELETED files. DiscUtils mounts FAT but only enumerates
// live entries, so deleted recovery requires parsing directories directly: a directory
// entry whose first byte is 0xE5 is deleted. FAT zeroes the cluster chain on delete, so
// recovery is best-effort and assumes the file was stored contiguously (read `size` bytes
// forward from the recorded start cluster).
public static class FatDeletedFileScanner
{
    public sealed record FatDeletedEntry(string Name, long StartCluster, long Size);

    public sealed record FatGeometry(
        int BytesPerSector, int SectorsPerCluster, long ClusterSize,
        long FatByteOffset, long FatSizeBytes, int FatType, // 12 / 16 / 32
        long RootDirByteOffset, int RootEntryCount, long RootCluster,
        long FirstDataByteOffset);

    public static FatGeometry? ReadGeometry(System.IO.Stream vol)
    {
        try
        {
            vol.Seek(0, System.IO.SeekOrigin.Begin);
            var boot = new byte[512];
            if (ReadFully(vol, boot, 0, boot.Length) != boot.Length) return null;

            int bps          = U16(boot, 0x0B);
            int spc          = boot[0x0D];
            int reserved     = U16(boot, 0x0E);
            int numFats      = boot[0x10];
            int rootEntCnt   = U16(boot, 0x11);
            int sectorsPerFat16 = U16(boot, 0x16);
            long totalSec16  = U16(boot, 0x13);
            long totalSec32  = U32(boot, 0x20);
            long sectorsPerFat32 = U32(boot, 0x24);
            long rootCluster = U32(boot, 0x2C);

            if (bps == 0 || spc == 0 || numFats == 0) return null;

            long sectorsPerFat = sectorsPerFat16 != 0 ? sectorsPerFat16 : sectorsPerFat32;
            long totalSectors  = totalSec16 != 0 ? totalSec16 : totalSec32;
            if (sectorsPerFat == 0 || totalSectors == 0) return null;

            long rootDirSectors = ((long)rootEntCnt * 32 + (bps - 1)) / bps; // 0 for FAT32
            long firstDataSector = reserved + (long)numFats * sectorsPerFat + rootDirSectors;
            long dataSectors = totalSectors - firstDataSector;
            if (dataSectors <= 0) return null;
            long countOfClusters = dataSectors / spc;

            int fatType = countOfClusters < 4085 ? 12 : countOfClusters < 65525 ? 16 : 32;

            long fatByteOffset = (long)reserved * bps;
            long rootDirByteOffset = (reserved + (long)numFats * sectorsPerFat) * bps; // FAT12/16 only

            return new FatGeometry(
                bps, spc, (long)spc * bps,
                fatByteOffset, sectorsPerFat * bps, fatType,
                rootDirByteOffset, rootEntCnt, rootCluster,
                firstDataSector * bps);
        }
        catch { return null; }
    }

    public static long ClusterByteOffset(FatGeometry g, long cluster)
        => g.FirstDataByteOffset + (cluster - 2) * g.ClusterSize;

    // Recursively scans all directories for deleted entries with a matching extension.
    public static IReadOnlyList<FatDeletedEntry> Scan(System.IO.Stream vol, FatGeometry g, ISet<string> mediaExtensions)
    {
        var results = new List<FatDeletedEntry>();
        var fat = ReadFat(vol, g);
        var visited = new HashSet<long>();

        // Root directory bytes.
        byte[] rootDir = g.FatType == 32
            ? ReadClusterChain(vol, g, fat, g.RootCluster, visited)
            : ReadFixedRoot(vol, g);

        ScanDirectory(vol, g, fat, rootDir, mediaExtensions, results, visited, depth: 0);
        return results;
    }

    public static byte[]? RecoverContiguous(System.IO.Stream vol, FatGeometry g, long startCluster, long size, long maxBytes)
    {
        if (startCluster < 2 || size <= 0 || size > maxBytes) return null;
        long offset = ClusterByteOffset(g, startCluster);
        var buf = new byte[(int)size];
        vol.Seek(offset, System.IO.SeekOrigin.Begin);
        return ReadFully(vol, buf, 0, buf.Length) == buf.Length ? buf : null;
    }

    private static void ScanDirectory(
        System.IO.Stream vol, FatGeometry g, uint[] fat, byte[] dir,
        ISet<string> mediaExtensions, List<FatDeletedEntry> results, HashSet<long> visited, int depth)
    {
        if (depth > 64) return; // guard against malformed loops

        for (int o = 0; o + 32 <= dir.Length; o += 32)
        {
            byte first = dir[o];
            if (first == 0x00) break;       // end of directory
            byte attr = dir[o + 11];
            if (attr == 0x0F) continue;     // long-file-name entry
            if ((attr & 0x08) != 0) continue; // volume label

            long startCluster = U16(dir, o + 0x1A) | ((long)U16(dir, o + 0x14) << 16);

            if ((attr & 0x10) != 0)
            {
                // Subdirectory — recurse into LIVE dirs (deleted dirs have broken chains).
                if (first == 0xE5 || first == (byte)'.') continue;
                if (startCluster < 2) continue;
                var child = ReadClusterChain(vol, g, fat, startCluster, visited);
                if (child.Length > 0)
                    ScanDirectory(vol, g, fat, child, mediaExtensions, results, visited, depth + 1);
                continue;
            }

            if (first != 0xE5) continue;    // only deleted files

            var name = ParseShortName(dir, o);
            var ext = System.IO.Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext) || !mediaExtensions.Contains(ext)) continue;

            long size = U32(dir, o + 0x1C);
            if (size <= 0 || startCluster < 2) continue;

            results.Add(new FatDeletedEntry(name, startCluster, size));
        }
    }

    // Reconstructs the 8.3 name. The first character was overwritten by the 0xE5 delete
    // marker and is lost, so it is shown as '_'.
    private static string ParseShortName(byte[] dir, int o)
    {
        Span<char> baseName = stackalloc char[8];
        for (int i = 0; i < 8; i++)
        {
            byte c = dir[o + i];
            baseName[i] = i == 0 ? '_' : (c == 0x20 ? ' ' : (char)c);
        }
        var name = new string(baseName).TrimEnd();

        Span<char> ext = stackalloc char[3];
        for (int i = 0; i < 3; i++)
        {
            byte c = dir[o + 8 + i];
            ext[i] = c == 0x20 ? ' ' : (char)c;
        }
        var extension = new string(ext).TrimEnd();
        return extension.Length > 0 ? $"{name}.{extension}" : name;
    }

    private static byte[] ReadFixedRoot(System.IO.Stream vol, FatGeometry g)
    {
        var buf = new byte[g.RootEntryCount * 32];
        vol.Seek(g.RootDirByteOffset, System.IO.SeekOrigin.Begin);
        ReadFully(vol, buf, 0, buf.Length);
        return buf;
    }

    private static byte[] ReadClusterChain(System.IO.Stream vol, FatGeometry g, uint[] fat, long startCluster, HashSet<long> visited)
    {
        var ms = new System.IO.MemoryStream();
        long cluster = startCluster;
        int guard = 0;
        uint endMarker = g.FatType == 12 ? 0xFF8u : g.FatType == 16 ? 0xFFF8u : 0x0FFFFFF8u;

        while (cluster >= 2 && cluster < fat.LongLength && guard++ < 1 << 20)
        {
            if (!visited.Add(cluster)) break; // loop / shared cluster
            var clusterBuf = new byte[g.ClusterSize];
            vol.Seek(ClusterByteOffset(g, cluster), System.IO.SeekOrigin.Begin);
            if (ReadFully(vol, clusterBuf, 0, clusterBuf.Length) != clusterBuf.Length) break;
            ms.Write(clusterBuf, 0, clusterBuf.Length);

            uint next = fat[cluster];
            if (next >= endMarker || next < 2) break;
            cluster = next;
        }
        return ms.ToArray();
    }

    private static uint[] ReadFat(System.IO.Stream vol, FatGeometry g)
    {
        var raw = new byte[(int)Math.Min(g.FatSizeBytes, int.MaxValue)];
        vol.Seek(g.FatByteOffset, System.IO.SeekOrigin.Begin);
        ReadFully(vol, raw, 0, raw.Length);

        long entries = g.FatType == 12 ? raw.Length * 2L / 3 : g.FatType == 16 ? raw.Length / 2L : raw.Length / 4L;
        var fat = new uint[Math.Min(entries, 1 << 24)];

        for (long i = 0; i < fat.LongLength; i++)
        {
            if (g.FatType == 12)
            {
                long b = i * 3 / 2;
                if (b + 1 >= raw.Length) break;
                uint pair = (uint)(raw[(int)b] | (raw[(int)b + 1] << 8));
                fat[i] = (i & 1) == 0 ? pair & 0x0FFF : (pair >> 4) & 0x0FFF;
            }
            else if (g.FatType == 16)
            {
                long b = i * 2;
                if (b + 1 >= raw.Length) break;
                fat[i] = (uint)(raw[(int)b] | (raw[(int)b + 1] << 8));
            }
            else
            {
                long b = i * 4;
                if (b + 3 >= raw.Length) break;
                fat[i] = U32(raw, (int)b) & 0x0FFFFFFF;
            }
        }
        return fat;
    }

    private static int  U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

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
