using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Sources.Carving;

/// <summary>A file recovered from raw bytes by signature carving (no filesystem metadata).</summary>
public sealed record CarvedArtifact(long Offset, long Length, FormatId Format, string Extension);

/// <summary>
/// Signature ("magic byte") carver: recovers whole files from a raw byte buffer by
/// recognising their start signatures and parsing their structure to the end, without
/// any filesystem metadata. v1 supports JPEG and PNG — the two common image formats
/// with an unambiguous, length/marker-walkable structure.
/// </summary>
public static class SignatureCarver
{
    // JPEG entropy-coded data can be long; cap a single carve so a corrupt/never-ending
    // stream can't run to the end of a huge image. 64 MiB comfortably exceeds real photos.
    private const long MaxArtifactBytes = 64L * 1024 * 1024;

    public static IReadOnlyList<CarvedArtifact> Carve(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var results = new List<CarvedArtifact>();

        int i = 0;
        while (i < data.Length)
        {
            // JPEG: FF D8 FF
            if (i + 2 < data.Length && data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF)
            {
                var end = FindJpegEnd(data, i);
                if (end > i)
                {
                    results.Add(new CarvedArtifact(i, end - i, FormatId.Jpeg, ".jpg"));
                    i = end;
                    continue;
                }
            }

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (i + 8 <= data.Length &&
                data[i] == 0x89 && data[i + 1] == 0x50 && data[i + 2] == 0x4E && data[i + 3] == 0x47 &&
                data[i + 4] == 0x0D && data[i + 5] == 0x0A && data[i + 6] == 0x1A && data[i + 7] == 0x0A)
            {
                var end = FindPngEnd(data, i);
                if (end > i)
                {
                    results.Add(new CarvedArtifact(i, end - i, FormatId.Png, ".png"));
                    i = end;
                    continue;
                }
            }

            i++;
        }

        return results;
    }

    // Walks JPEG marker segments from the SOI to the SOS, then scans the entropy-coded
    // data for the EOI. Returns the exclusive end offset, or -1 if the structure is invalid.
    private static int FindJpegEnd(byte[] data, int soi)
    {
        long limit = Math.Min(data.Length, soi + MaxArtifactBytes);
        int p = soi + 2; // past SOI (FF D8)

        // Marker segments: FF <marker> [2-byte big-endian length] [payload].
        while (p + 1 < limit)
        {
            if (data[p] != 0xFF) return -1;       // not aligned on a marker — give up
            byte marker = data[p + 1];

            // Padding 0xFF bytes before a marker are allowed.
            if (marker == 0xFF) { p++; continue; }

            // Standalone markers (no length): RSTn (D0–D7), TEM (01).
            if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0x01) { p += 2; continue; }

            // Start of Scan — entropy-coded data follows; scan for EOI.
            if (marker == 0xDA)
            {
                if (p + 4 > limit) return -1;
                int sosLen = (data[p + 2] << 8) | data[p + 3];
                int scan = p + 2 + sosLen;        // past the SOS header
                while (scan + 1 < limit)
                {
                    if (data[scan] == 0xFF)
                    {
                        byte b = data[scan + 1];
                        if (b == 0xD9) return scan + 2;                 // EOI — done
                        if (b == 0x00 || (b >= 0xD0 && b <= 0xD7)) { scan += 2; continue; } // stuffing / restart
                        scan += 2; continue;       // other marker inside the scan
                    }
                    scan++;
                }
                return -1;
            }

            // Any other marker: read its 2-byte length and skip the segment.
            if (p + 4 > limit) return -1;
            int segLen = (data[p + 2] << 8) | data[p + 3];
            if (segLen < 2) return -1;             // malformed length
            p += 2 + segLen;
        }

        return -1;
    }

    // Walks PNG chunks (4-byte BE length + 4-byte type + data + 4-byte CRC) to IEND.
    // Returns the exclusive end offset, or -1 if the structure is invalid.
    private static int FindPngEnd(byte[] data, int sig)
    {
        long limit = Math.Min(data.Length, sig + MaxArtifactBytes);
        int p = sig + 8; // past the 8-byte signature

        while (p + 8 <= limit)
        {
            long len = ((long)data[p] << 24) | ((long)data[p + 1] << 16) | ((long)data[p + 2] << 8) | data[p + 3];
            if (len < 0) return -1;

            bool isIend = data[p + 4] == 0x49 && data[p + 5] == 0x45 && data[p + 6] == 0x4E && data[p + 7] == 0x44;
            long chunkEnd = p + 8L + len + 4L;     // length + type + data + CRC
            if (chunkEnd > limit) return -1;

            if (isIend) return (int)chunkEnd;
            p = (int)chunkEnd;
        }

        return -1;
    }
}
