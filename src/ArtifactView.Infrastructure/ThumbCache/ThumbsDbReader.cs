using System.Text;
using OpenMcdf;

namespace ArtifactView.Infrastructure.ThumbCache;

// Reads thumbnail entries from a Windows Thumbs.db file.
//
// Thumbs.db is an OLE Compound Document (Microsoft Structured Storage)
// created by Windows XP/Vista/7 Explorer to cache folder thumbnails.
// Modern Windows 10+ uses thumbcache_*.db instead, but Thumbs.db files
// persist on external media (USB drives, SD cards, network shares) and
// are a key forensic source for ghost files.
//
// Internal layout:
//   "Catalog" stream  — maps numeric IDs to original filenames
//   "1", "2", …       — each holds a small header + JPEG (or BMP) payload
//
// The reader is read-only and never modifies the source file.
public static class ThumbsDbReader
{
    // JPEG SOI marker used to locate the image payload inside a thumbnail stream.
    private static ReadOnlySpan<byte> JpegSoi => [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// Reads all thumbnail entries from a Thumbs.db file.
    /// Returns an empty list for missing, corrupt, or unreadable files.
    /// </summary>
    public static IReadOnlyList<ThumbsDbEntry> ReadEntries(string dbPath)
    {
        if (!File.Exists(dbPath))
            return [];

        try
        {
            // Open with a shared-read stream so Explorer or antivirus locks
            // don't prevent reading.
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var cf = RootStorage.Open(fs, StorageModeFlags.LeaveOpen);

            var (catalog, catalogThumbWidth, catalogThumbHeight, catalogVersion) = ReadCatalog(cf);
            var entries = new List<ThumbsDbEntry>();

            foreach (var entry in cf.EnumerateEntries())
            {
                if (entry.Type != EntryType.Stream || !int.TryParse(entry.Name, out var idx))
                    continue;

                using var stream = cf.OpenStream(entry.Name);
                var data = ReadAllBytes(stream);
                if (data.Length < 12)
                    continue;

                var payloadStart = FindJpegStart(data);
                if (payloadStart < 0)
                    continue;

                var (filename, modified) = catalog.TryGetValue(idx, out var info)
                    ? info
                    : ($"#{idx}", (DateTime?)null);

                entries.Add(new ThumbsDbEntry(
                    OriginalFilename: filename,
                    StreamIndex:      idx,
                    Width:            catalogThumbWidth,
                    Height:           catalogThumbHeight,
                    PayloadSize:      data.Length - payloadStart,
                    StreamName:       entry.Name,
                    LastModifiedUtc:  modified,
                    CatalogVersion:   catalogVersion));
            }

            return entries;
        }
        catch
        {
            // Corrupt, locked, or not an OLE Compound Document.
            return [];
        }
    }

    /// <summary>
    /// Extracts the raw JPEG bytes for a specific entry.
    /// Returns null if the stream is missing or the payload cannot be located.
    /// </summary>
    public static byte[]? ExtractPayload(string dbPath, ThumbsDbEntry entry)
    {
        if (!File.Exists(dbPath))
            return null;

        try
        {
            using var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var cf = RootStorage.Open(fs, StorageModeFlags.LeaveOpen);

            if (!cf.TryOpenStream(entry.StreamName, out var stream))
                return null;

            using var _ = stream;
            var data  = ReadAllBytes(stream);
            var start = FindJpegStart(data);
            if (start < 0)
                return null;

            return data[start..];
        }
        catch
        {
            return null;
        }
    }

    // ── Catalog parsing ──────────────────────────────────────────────────

    // The Catalog stream maps stream indices to original filenames.
    // Header (16 bytes): uint16 headerSize, uint16 version,
    //                    uint32 entryCount, uint32 thumbWidth, uint32 thumbHeight
    // Each entry: uint32 entrySize, uint32 streamId,
    //             uint64 lastModified (FILETIME),
    //             wchar[] filename (null-terminated)
    private static readonly DateTime s_fileTimeEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (Dictionary<int, (string Name, DateTime? Modified)> Map,
                    int ThumbWidth, int ThumbHeight, ushort Version) ReadCatalog(RootStorage cf)
    {
        var map = new Dictionary<int, (string, DateTime?)>();

        if (!cf.TryOpenStream("Catalog", out var catalogStream))
            return (map, 0, 0, 0);

        using var _ = catalogStream;
        var data = ReadAllBytes(catalogStream);
        if (data.Length < 16)
            return (map, 0, 0, 0);

        var headerSize = BitConverter.ToUInt16(data, 0);
        var version    = BitConverter.ToUInt16(data, 2);
        if (headerSize < 16 || headerSize > data.Length)
            return (map, 0, 0, version);

        var thumbWidth  = BitConverter.ToInt32(data, 8);
        var thumbHeight = BitConverter.ToInt32(data, 12);

        int pos = headerSize;

        while (pos + 16 < data.Length)
        {
            var entrySize = BitConverter.ToInt32(data, pos);
            if (entrySize < 16 || pos + entrySize > data.Length)
                break;

            var streamId = BitConverter.ToInt32(data, pos + 4);

            // FILETIME at pos + 8: 100-nanosecond intervals since 1601-01-01 UTC.
            var fileTime = BitConverter.ToInt64(data, pos + 8);
            DateTime? modified = fileTime > 0
                ? s_fileTimeEpoch.AddTicks(fileTime)
                : null;

            // Filename at pos + 16, UTF-16LE, null-terminated.
            var nameStart = pos + 16;
            var nameEnd   = nameStart;
            while (nameEnd + 1 < pos + entrySize)
            {
                if (data[nameEnd] == 0 && data[nameEnd + 1] == 0)
                    break;
                nameEnd += 2;
            }

            var filename = nameEnd > nameStart
                ? Encoding.Unicode.GetString(data, nameStart, nameEnd - nameStart)
                : string.Empty;

            map[streamId] = (filename, modified);

            pos += entrySize;
        }

        return (map, thumbWidth, thumbHeight, version);
    }

    // Scans the first 256 bytes for the JPEG SOI marker (FF D8 FF).
    // Returns the byte offset of FF D8, or -1 if not found.
    private static int FindJpegStart(byte[] data)
    {
        var limit = Math.Min(data.Length - 2, 256);
        for (var i = 0; i < limit; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF)
                return i;
        }
        return -1;
    }

    private static byte[] ReadAllBytes(System.IO.Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
