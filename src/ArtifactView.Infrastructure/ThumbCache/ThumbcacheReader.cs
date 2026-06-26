using System.Buffers.Binary;
using System.Text;

namespace ArtifactView.Infrastructure.ThumbCache;

// Reads Windows thumbcache_*.db files (Vista / 7 / 8 / 10 / 11).
//
// File layout (simplified):
//   [File header: 24–32 bytes]
//     Signature  "CMMM" (4 bytes)
//     Version    uint32 LE
//     CacheType  uint32 LE  (32=thumbcache_32.db, 96, 256, 1024, 2560, sr, wide, etc.)
//     ...padding/reserved
//   [Entry 0]
//     Signature  "CMMM" (4 bytes)
//     Size       uint32 LE  (total entry size including this header)
//     Hash       uint64 LE  (content hash — used to correlate with the _idx file)
//     Filename   32 bytes wchar (null-terminated) — ONLY in version >= 21 (Win 8+)
//                (earlier versions store the hash only; filename lives in the _idx DB)
//     DataSize   uint32 LE  (thumbnail payload byte count)
//     HeaderSize uint32 LE  (entry header size before the payload bytes)
//     ...padding
//     [Payload: DataSize bytes — usually JPEG or PNG]
//   [Entry 1]
//     ...
//
// This reader enumerates entries and extracts their payloads as byte arrays.
// It does NOT parse the thumbcache_idx.db (hash→filename index).
//
// Reference: https://github.com/thumbcacheviewer/thumbcacheviewer
public static class ThumbcacheReader
{
    private static ReadOnlySpan<byte> Signature => "CMMM"u8;

    // Version thresholds — the entry format changed across Windows releases.
    private const uint VersionVistaWin7 = 20;   // No embedded filename in entries
    private const uint VersionWin8Plus  = 21;   // 32-char wchar filename in each entry
    private const uint VersionWin10     = 30;   // Wider file header (32 bytes), same entry layout
    private const uint VersionWin11     = 32;   // Same entry layout as Win10; file header still 32 bytes

    /// <summary>
    /// Reads the file header and returns the thumbcache version, or null
    /// if the file is not a valid thumbcache_*.db.
    /// </summary>
    public static uint? ReadVersion(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[8];
            if (fs.Read(header) < 8) return null;
            if (!header[..4].SequenceEqual(Signature)) return null;
            return BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        }
        catch { return null; }
    }

    /// <summary>
    /// Enumerates all non-empty cache entries in a thumbcache_*.db file.
    /// Entries with DataSize == 0 (deleted placeholders) are skipped.
    /// </summary>
    public static IReadOnlyList<ThumbcacheEntry> ReadEntries(string path)
    {
        var results = new List<ThumbcacheEntry>();

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> fileHeader = stackalloc byte[8];
            if (fs.Read(fileHeader) < 8) return results;
            if (!fileHeader[..4].SequenceEqual(Signature)) return results;
            var version = BinaryPrimitives.ReadUInt32LittleEndian(fileHeader[4..]);

            // Skip the rest of the file header (variable length by version).
            var fileHeaderSize = version >= VersionWin10 ? 32 : 24; // Win10/Win11 = 32, earlier = 24
            fs.Position = fileHeaderSize;

            // Pre-allocate an entry header buffer large enough for any version.
            var entryBuf = new byte[200];

            while (fs.Position < fs.Length - 8)
            {
                var entryOffset = fs.Position;
                if (fs.Read(entryBuf, 0, 8) < 8) break;

                // Each entry starts with "CMMM" + uint32 entry size.
                if (entryBuf[0] != 0x43 || entryBuf[1] != 0x4D ||
                    entryBuf[2] != 0x4D || entryBuf[3] != 0x4D)
                    break; // corrupt or end of entries

                var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(entryBuf.AsSpan(4));
                if (entrySize < 8 || entryOffset + entrySize > fs.Length)
                    break;

                // Read the rest of the entry header (up to our buffer or entrySize).
                var remaining = (int)Math.Min(entrySize - 8, entryBuf.Length - 8);
                if (fs.Read(entryBuf, 8, remaining) < remaining) break;

                // Parse fields based on version.
                ulong hash;
                int dataSize, headerSize;
                string filename;

                if (version >= VersionWin8Plus)
                {
                    // Offset 8: hash (8 bytes)
                    hash = BinaryPrimitives.ReadUInt64LittleEndian(entryBuf.AsSpan(8));
                    // Offset 16: wchar filename (32 wchars = 64 bytes, null-terminated)
                    filename = ReadWCharString(entryBuf.AsSpan(16, 64));
                    // Offset 80: data size (4 bytes)
                    dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(entryBuf.AsSpan(80));
                    // Offset 84: header size (4 bytes)
                    headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(entryBuf.AsSpan(84));
                }
                else
                {
                    // Vista/7 format: no filename field.
                    hash = BinaryPrimitives.ReadUInt64LittleEndian(entryBuf.AsSpan(8));
                    // The exact offsets vary but data size and header size are at known positions.
                    dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(entryBuf.AsSpan(16));
                    headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(entryBuf.AsSpan(20));
                    filename = string.Empty;
                }

                // Skip entries with no payload (deleted/empty placeholders).
                if (dataSize > 0 && headerSize > 0)
                {
                    results.Add(new ThumbcacheEntry(
                        entryOffset, hash, dataSize, headerSize, filename));
                }

                // Advance to the next entry.
                fs.Position = entryOffset + entrySize;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThumbcacheReader] ReadEntries failed for '{path}': {ex.GetType().Name}: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Extracts the raw thumbnail payload for a specific entry.
    /// Returns the exact bytes stored in the cache — usually JPEG or PNG.
    /// </summary>
    public static byte[]? ExtractPayload(string path, ThumbcacheEntry entry) =>
        ExtractPayloadDirect(path, entry.EntryOffset + entry.HeaderSize, entry.DataSize);

    /// <summary>
    /// Extracts a payload by direct seek — avoids re-reading all entries.
    /// Use when the payload offset and size are already known (e.g. cached
    /// on <see cref="ArtifactView.Core.Models.MediaEntityRow"/>).
    /// </summary>
    public static byte[]? ExtractPayloadDirect(string path, long payloadOffset, int dataSize)
    {
        if (dataSize <= 0) return null;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (payloadOffset + dataSize > fs.Length) return null;

            fs.Position = payloadOffset;
            var buffer = new byte[dataSize];
            var read   = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read == buffer.Length ? buffer : null;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ThumbcacheReader] ExtractPayloadDirect failed for '{path}': {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Discovers all thumbcache_*.db files in the default Windows Explorer cache folder.
    /// </summary>
    public static IReadOnlyList<string> DiscoverDefaultPaths()
    {
        var explorerCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");

        if (!Directory.Exists(explorerCache))
            return [];

        return Directory.GetFiles(explorerCache, "thumbcache_*.db")
            .Where(f => ReadVersion(f) is not null)
            .OrderBy(f => f)
            .ToArray();
    }

    private static string ReadWCharString(Span<byte> data)
    {
        // UTF-16 LE, null-terminated.
        var chars = new char[data.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            var c = (char)(data[i * 2] | (data[i * 2 + 1] << 8));
            if (c == '\0') return new string(chars, 0, i);
            chars[i] = c;
        }
        return new string(chars);
    }
}
