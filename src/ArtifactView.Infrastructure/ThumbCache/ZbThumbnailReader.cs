using System.Buffers.Binary;
using System.Text;

namespace ArtifactView.Infrastructure.ThumbCache;

// Reads thumbnail entries from ZbThumbnail.info files.
//
// ZbThumbnail.info is a per-folder thumbnail cache created by Zoner Photo Studio.
// It stores small JPEG previews of images that were browsed in the folder.
// Like Thumbs.db, these files persist on external media after the originals
// are deleted and are valuable forensic sources for ghost file detection.
//
// File layout:
//   [Header: 8 bytes]
//     Version    uint32 LE  (typically 1 or 2)
//     EntryCount uint32 LE
//   [Entry 0]
//     FilenameLen  uint16 LE  (byte count)
//     Filename     byte[]     (UTF-8 or system codepage)
//     OrigFileSize uint32 LE
//     ModTime      uint64 LE  (Windows FILETIME: 100-ns since 1601-01-01)
//     ThumbWidth   uint16 LE
//     ThumbHeight  uint16 LE
//     PayloadSize  uint32 LE  (JPEG byte count)
//     Payload      byte[]     (JPEG data)
//   [Entry 1]
//     ...
//
// The reader is read-only and never modifies the source file.
public static class ZbThumbnailReader
{
    private static readonly DateTime s_fileTimeEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Reads all thumbnail entries from a ZbThumbnail.info file.
    /// Returns an empty list for missing, corrupt, or unreadable files.
    /// </summary>
    public static IReadOnlyList<ZbThumbnailEntry> ReadEntries(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 8)
                return [];

            Span<byte> header = stackalloc byte[8];
            if (fs.Read(header) < 8)
                return [];

            var version    = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);

            // Sanity: version should be small, entry count reasonable.
            if (version is < 1 or > 10)
                return [];
            if (entryCount > 100_000)
                return [];

            var entries = new List<ZbThumbnailEntry>((int)Math.Min(entryCount, 1000));
            Span<byte> buf = stackalloc byte[8];

            for (uint i = 0; i < entryCount && fs.Position < fs.Length - 4; i++)
            {
                // Filename length (2 bytes).
                if (fs.Read(buf[..2]) < 2) break;
                var filenameLen = BinaryPrimitives.ReadUInt16LittleEndian(buf);
                if (filenameLen == 0 || filenameLen > 512) break;

                // Filename bytes.
                var filenameBytes = new byte[filenameLen];
                if (fs.ReadAtLeast(filenameBytes, filenameLen, throwOnEndOfStream: false) < filenameLen)
                    break;

                // Decode filename — try UTF-8 first, fall back to Latin-1
                // (Zoner Photo Studio originated in the Czech Republic and
                // older versions used the system codepage).
                var filename = Encoding.UTF8.GetString(filenameBytes);
                if (filename.Contains('\uFFFD'))
                    filename = Encoding.Latin1.GetString(filenameBytes);

                // Original file size (4 bytes).
                if (fs.Read(buf[..4]) < 4) break;
                var origSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(buf);

                // Modification time (8 bytes, Windows FILETIME).
                if (fs.Read(buf) < 8) break;
                var fileTime = BinaryPrimitives.ReadInt64LittleEndian(buf);
                DateTime? modified = fileTime > 0
                    ? s_fileTimeEpoch.AddTicks(fileTime)
                    : null;

                // Thumbnail width and height (2 + 2 bytes).
                if (fs.Read(buf[..4]) < 4) break;
                var width  = BinaryPrimitives.ReadUInt16LittleEndian(buf);
                var height = BinaryPrimitives.ReadUInt16LittleEndian(buf[2..]);

                // JPEG payload size (4 bytes).
                if (fs.Read(buf[..4]) < 4) break;
                var payloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(buf);
                if (payloadSize <= 0 || fs.Position + payloadSize > fs.Length)
                    break;

                var payloadOffset = fs.Position;

                entries.Add(new ZbThumbnailEntry(
                    filename, width, height, origSize, modified,
                    payloadOffset, payloadSize));

                // Skip past the payload to the next entry.
                fs.Position = payloadOffset + payloadSize;
            }

            return entries;
        }
        catch
        {
            // Corrupt or unreadable — best-effort.
            return [];
        }
    }

    /// <summary>
    /// Extracts the raw JPEG payload for a specific entry.
    /// </summary>
    public static byte[]? ExtractPayload(string path, ZbThumbnailEntry entry) =>
        ExtractPayloadDirect(path, entry.PayloadOffset, entry.PayloadSize);

    /// <summary>
    /// Extracts a payload by direct offset and size — avoids re-reading entries.
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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZbThumbnailReader] ExtractPayload failed for '{path}': {ex.GetType().Name}: {ex.Message}"); return null; }
    }
}
