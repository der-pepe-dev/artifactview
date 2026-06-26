using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Raw;
using DiscUtils.Streams;

namespace ArtifactView.Infrastructure.Sources.DiskImage;

// Mounts partitions from a raw disk image and enumerates media files.
public static class DiskImagePartitionReader
{
    private static readonly HashSet<string> s_mediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".gif", ".bmp", ".tif", ".tiff",
        ".webp", ".avif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf",
        ".mp4", ".mov", ".m4v", ".3gp", ".avi", ".mkv"
    };

    public sealed record DiskImageFileEntry(
        string LogicalPath,
        long SizeBytes,
        DateTime? CreatedUtc,
        DateTime? ModifiedUtc,
        string Filesystem,
        int PartitionIndex,
        bool IsDeleted
    );

    // Opens a raw .dd/.img disk image and enumerates all media files across all partitions.
    // Yields live files. Deleted NTFS files are included via separate MFT scan.
    public static IReadOnlyList<DiskImageFileEntry> ReadAllMediaFiles(string imagePath)
    {
        var results = new List<DiskImageFileEntry>();

        try
        {
            using var imageStream = new System.IO.FileStream(
                imagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            using var disk = new Disk(imageStream, Ownership.None);

            var partitions = TryGetPartitions(disk);
            if (partitions.Count == 0)
            {
                // No partition table — treat whole image as single partition.
                try
                {
                    imageStream.Seek(0, System.IO.SeekOrigin.Begin);
                    ReadPartition(imageStream, 0, results);
                }
                catch { }
            }
            else
            {
                for (int i = 0; i < partitions.Count; i++)
                {
                    try
                    {
                        using var partStream = partitions[i].Open();
                        ReadPartition(partStream, i, results);
                    }
                    catch { }
                }
            }
        }
        catch { }

        return results;
    }

    // Reads the full content of a single LIVE file from the image, given its partition,
    // internal path, and filesystem (as reported by ReadAllMediaFiles). Returns null on
    // any failure or when the file is larger than maxBytes. Deleted files are not
    // recoverable here (no data-run reconstruction).
    public static byte[]? ReadFileBytes(
        string imagePath, int partitionIndex, string internalPath, string filesystem,
        long maxBytes = 512L * 1024 * 1024)
    {
        try
        {
            using var imageStream = new System.IO.FileStream(
                imagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            using var disk = new Disk(imageStream, Ownership.None);

            var partitions = TryGetPartitions(disk);
            if (partitions.Count == 0)
            {
                imageStream.Seek(0, System.IO.SeekOrigin.Begin);
                return ReadFileFromPartition(imageStream, internalPath, filesystem, maxBytes);
            }

            if (partitionIndex < 0 || partitionIndex >= partitions.Count)
                return null;

            using var partStream = partitions[partitionIndex].Open();
            return ReadFileFromPartition(partStream, internalPath, filesystem, maxBytes);
        }
        catch { return null; }
    }

    private static byte[]? ReadFileFromPartition(
        System.IO.Stream partStream, string internalPath, string filesystem, long maxBytes)
    {
        try
        {
            if (string.Equals(filesystem, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                partStream.Seek(0, System.IO.SeekOrigin.Begin);
                if (!NtfsFileSystem.Detect(partStream)) return null;
                partStream.Seek(0, System.IO.SeekOrigin.Begin);
                using var ntfs = new NtfsFileSystem(partStream);
                return ReadFile(ntfs, internalPath, maxBytes);
            }

            if (string.Equals(filesystem, "FAT", StringComparison.OrdinalIgnoreCase))
            {
                partStream.Seek(0, System.IO.SeekOrigin.Begin);
                if (!FatFileSystem.Detect(partStream)) return null;
                partStream.Seek(0, System.IO.SeekOrigin.Begin);
                using var fat = new FatFileSystem(partStream);
                return ReadFile(fat, internalPath, maxBytes);
            }

            return null;
        }
        catch { return null; }
    }

    private static byte[]? ReadFile(DiscFileSystem fs, string path, long maxBytes)
    {
        if (!fs.FileExists(path)) return null;
        var len = fs.GetFileLength(path);
        if (len <= 0 || len > maxBytes) return null;

        using var s = fs.OpenFile(path, System.IO.FileMode.Open, System.IO.FileAccess.Read);
        var buf = new byte[(int)len];
        ReadFully(s, buf, 0, buf.Length);
        return buf;
    }

    private static IReadOnlyList<PartitionInfo> TryGetPartitions(Disk disk)
    {
        // Try MBR first, then GPT.
        try
        {
            var mbr = new BiosPartitionTable(disk);
            if (mbr.Partitions.Count > 0)
                return [.. mbr.Partitions];
        }
        catch { }

        try
        {
            var gpt = new GuidPartitionTable(disk);
            if (gpt.Partitions.Count > 0)
                return [.. gpt.Partitions];
        }
        catch { }

        // No partition table: treat entire image as one partition.
        return [];
    }

    private static void ReadPartition(System.IO.Stream partStream, int partIndex, List<DiskImageFileEntry> results)
    {
        // Try NTFS.
        if (TryReadNtfs(partStream, partIndex, results)) return;
        // Try FAT.
        TryReadFat(partStream, partIndex, results);
    }

    private static bool TryReadNtfs(System.IO.Stream partStream, int partIndex, List<DiskImageFileEntry> results)
    {
        try
        {
            partStream.Seek(0, System.IO.SeekOrigin.Begin);
            if (!NtfsFileSystem.Detect(partStream)) return false;
            partStream.Seek(0, System.IO.SeekOrigin.Begin);

            using var ntfs = new NtfsFileSystem(partStream);
            EnumerateNtfsFiles(ntfs, @"\", partIndex, results);

            // Scan MFT for deleted files.
            ScanNtfsDeletedFiles(ntfs, partIndex, results);

            return true;
        }
        catch { return false; }
    }

    private static void EnumerateNtfsFiles(NtfsFileSystem ntfs, string dir, int partIndex, List<DiskImageFileEntry> results)
    {
        try
        {
            foreach (var file in ntfs.GetFiles(dir, "*", System.IO.SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var ext = System.IO.Path.GetExtension(file);
                    if (!s_mediaExtensions.Contains(ext)) continue;

                    var size    = ntfs.GetFileLength(file);
                    var created = ntfs.GetCreationTimeUtc(file);
                    var modified= ntfs.GetLastWriteTimeUtc(file);

                    results.Add(new DiskImageFileEntry(
                        file, size, created, modified, "NTFS", partIndex, IsDeleted: false));
                }
                catch { }
            }

            foreach (var sub in ntfs.GetDirectories(dir, "*", System.IO.SearchOption.TopDirectoryOnly))
            {
                // Skip NTFS metadata directories (start with $).
                if (System.IO.Path.GetFileName(sub).StartsWith('$')) continue;
                EnumerateNtfsFiles(ntfs, sub, partIndex, results);
            }
        }
        catch { }
    }

    private static void ScanNtfsDeletedFiles(NtfsFileSystem ntfs, int partIndex, List<DiskImageFileEntry> results)
    {
        // Access the raw $MFT to find deleted records.
        // DiscUtils doesn't expose deleted files via the public API, so we read
        // the $MFT stream directly and parse each 1024-byte record.
        try
        {
            if (!ntfs.FileExists(@"\$MFT")) return;
            using var mftStream = ntfs.OpenFile(@"\$MFT", System.IO.FileMode.Open, System.IO.FileAccess.Read);

            var record = new byte[1024];
            while (true)
            {
                var read = ReadFully(mftStream, record, 0, record.Length);
                if (read < record.Length) break;

                // Signature must be "FILE".
                if (record[0] != 'F' || record[1] != 'I' || record[2] != 'L' || record[3] != 'E')
                    continue;

                // Flags at offset 22: bit 0 = IN_USE, bit 1 = IS_DIRECTORY.
                var flags = BitConverter.ToUInt16(record, 22);
                bool inUse    = (flags & 0x01) != 0;
                bool isDir    = (flags & 0x02) != 0;
                if (inUse || isDir) continue; // skip live files and directories

                var filename = ExtractMftFilename(record);
                if (filename is null) continue;

                var ext = System.IO.Path.GetExtension(filename);
                if (!s_mediaExtensions.Contains(ext)) continue;

                // Don't duplicate files already found via live enumeration.
                var logicalPath = @"\[DELETED]\" + filename;
                if (results.Any(r => string.Equals(
                    System.IO.Path.GetFileName(r.LogicalPath), filename,
                    StringComparison.OrdinalIgnoreCase) && !r.IsDeleted))
                    continue;

                results.Add(new DiskImageFileEntry(
                    logicalPath, 0, null, null, "NTFS", partIndex, IsDeleted: true));
            }
        }
        catch { }
    }

    // Parses the $FILE_NAME attribute (type 0x30) from an MFT record to extract the filename.
    private static string? ExtractMftFilename(byte[] record)
    {
        try
        {
            int attrOffset = BitConverter.ToUInt16(record, 20);
            while (attrOffset + 4 < record.Length)
            {
                var attrType = BitConverter.ToUInt32(record, attrOffset);
                if (attrType == 0xFFFFFFFF) break; // end marker

                int attrLen = (int)BitConverter.ToUInt32(record, attrOffset + 4);
                if (attrLen == 0 || attrOffset + attrLen > record.Length) break;

                // $FILE_NAME attribute type = 0x30
                if (attrType == 0x30)
                {
                    var nonResidentFlag = record[attrOffset + 8];
                    if (nonResidentFlag != 0) { attrOffset += attrLen; continue; }

                    int contentOffset = BitConverter.ToUInt16(record, attrOffset + 20);
                    int contentStart  = attrOffset + contentOffset;

                    // $FILE_NAME content: 8+8+8+8+8+8+8+4+4+1+1 = 66 bytes header, then filename
                    // Offset 64 from content start = filename length in characters (1 byte)
                    // Offset 65 = filename namespace
                    // Offset 66 = filename (UTF-16LE)
                    if (contentStart + 66 >= record.Length) { attrOffset += attrLen; continue; }

                    int fnLen = record[contentStart + 64]; // characters, not bytes
                    var ns    = record[contentStart + 65]; // 0=POSIX, 1=Win32, 2=DOS, 3=Win32&DOS
                    // Prefer Win32 (ns=1) or Win32&DOS (ns=3) names; skip DOS-only (ns=2).
                    if (ns == 2) { attrOffset += attrLen; continue; }

                    int nameStart = contentStart + 66;
                    if (nameStart + fnLen * 2 > record.Length) { attrOffset += attrLen; continue; }

                    return System.Text.Encoding.Unicode.GetString(record, nameStart, fnLen * 2);
                }

                attrOffset += attrLen;
            }
        }
        catch { }
        return null;
    }

    private static bool TryReadFat(System.IO.Stream partStream, int partIndex, List<DiskImageFileEntry> results)
    {
        try
        {
            partStream.Seek(0, System.IO.SeekOrigin.Begin);
            if (!FatFileSystem.Detect(partStream)) return false;
            partStream.Seek(0, System.IO.SeekOrigin.Begin);

            using var fat = new FatFileSystem(partStream);
            EnumerateFatFiles(fat, @"\", partIndex, results);
            return true;
        }
        catch { return false; }
    }

    private static void EnumerateFatFiles(FatFileSystem fat, string dir, int partIndex, List<DiskImageFileEntry> results)
    {
        try
        {
            // FAT uses DOS wildcard semantics: "*" matches base-name only; "*.*" matches all files.
            foreach (var file in fat.GetFiles(dir, "*.*", System.IO.SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var ext = System.IO.Path.GetExtension(file);
                    if (!s_mediaExtensions.Contains(ext)) continue;

                    var size     = fat.GetFileLength(file);
                    var modified = fat.GetLastWriteTimeUtc(file);

                    results.Add(new DiskImageFileEntry(
                        file, size, null, modified, "FAT", partIndex, IsDeleted: false));
                }
                catch { }
            }

            foreach (var sub in fat.GetDirectories(dir, "*", System.IO.SearchOption.TopDirectoryOnly))
                EnumerateFatFiles(fat, sub, partIndex, results);
        }
        catch { }
    }

    private static int ReadFully(System.IO.Stream s, byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            var n = s.Read(buf, offset + total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
