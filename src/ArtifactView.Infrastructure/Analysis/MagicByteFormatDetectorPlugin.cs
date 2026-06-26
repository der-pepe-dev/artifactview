using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Analysis;

// Implements the IFormatDetector contract using the static MagicByteFormatDetector.
// The contract accepts a Stream; detection reads only the first 12 bytes.
public sealed class MagicByteFormatDetectorPlugin : IFormatDetector
{
    public ValueTask<FormatDetectionResult?> DetectAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = DetectFromStream(stream);
        return ValueTask.FromResult(result);
    }

    private static FormatDetectionResult? DetectFromStream(Stream stream)
    {
        try
        {
            if (stream.Length < 4) return null;
        }
        catch { /* non-seekable — try anyway */ }

        Span<byte> header = stackalloc byte[12];
        var pos  = TryGetPosition(stream);
        var read = stream.Read(header);
        if (pos.HasValue) TrySetPosition(stream, pos.Value);

        if (read < 4) return null;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return Make("jpeg", "image", "image/jpeg");

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return Make("png", "image", "image/png");

        // GIF: "GIF8"
        if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
            return Make("gif", "image", "image/gif");

        // BMP: "BM"
        if (header[0] == 0x42 && header[1] == 0x4D)
            return Make("bmp", "image", "image/bmp");

        // TIFF: "II" LE or "MM" BE
        if (read >= 4 &&
            ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00) ||
             (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
            return Make("tiff", "image", "image/tiff");

        // WebP: "RIFF" + 4-byte size + "WEBP"
        if (read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return Make("webp", "image", "image/webp");

        // ISO Base Media: "ftyp" at offset 4
        if (read >= 12 &&
            header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
        {
            var brand = System.Text.Encoding.ASCII.GetString(header[8..12]);
            return brand switch
            {
                "heic" or "heix" or "mif1" => Make("heif",    "image", "image/heic"),
                "avif" or "avis"           => Make("avif",    "image", "image/avif"),
                "mp41" or "mp42" or "isom" => Make("isobmff", "video", "video/mp4"),
                "qt  "                     => Make("isobmff", "video", "video/quicktime"),
                _                          => Make("isobmff", "media", null)
            };
        }

        return null;
    }

    private static FormatDetectionResult Make(string id, string family, string? mime) =>
        new() { FormatId = id, Family = family, MimeType = mime };

    private static long? TryGetPosition(Stream s)
    {
        try { return s.CanSeek ? s.Position : null; }
        catch { return null; }
    }

    private static void TrySetPosition(Stream s, long pos)
    {
        try { if (s.CanSeek) s.Position = pos; }
        catch { }
    }
}
