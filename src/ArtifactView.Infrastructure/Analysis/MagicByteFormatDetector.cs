namespace ArtifactView.Infrastructure.Analysis;

// Identifies image file format from magic bytes (file signature).
// Extension-independent: detects the actual content type regardless of what
// the file is named.  Returns null for unrecognised signatures.
//
// This is deliberately minimal — only covers common image/media formats.
// Plugin format handlers can extend detection for additional types.
public static class MagicByteFormatDetector
{
    public static FormatId? Detect(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 4)
                return null;

            Span<byte> header = stackalloc byte[12];
            var read = fs.Read(header);
            if (read < 4)
                return null;

            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return FormatId.Jpeg;

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (read >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return FormatId.Png;

            // GIF: "GIF87a" or "GIF89a"
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
                return FormatId.Gif;

            // BMP: "BM"
            if (header[0] == 0x42 && header[1] == 0x4D)
                return FormatId.Bmp;

            // TIFF: "II" (little-endian) or "MM" (big-endian) + 42
            if (read >= 4 &&
                ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00) ||
                 (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
                return FormatId.Tiff;

            // WebP: "RIFF" + 4 bytes size + "WEBP"
            if (read >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return FormatId.WebP;

            // HEIF/HEIC/AVIF: ISO-BMFF "ftyp" box at offset 4
            if (read >= 12 &&
                header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
            {
                // Brand at bytes 8–11 identifies the specific format
                var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
                return brand switch
                {
                    "heic" or "heix" or "mif1" => FormatId.Heif,
                    "avif" or "avis"           => FormatId.Avif,
                    _                          => FormatId.IsoBmff // generic ISO-BMFF container
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Maps an extension to the expected format for mismatch comparison.
    public static FormatId? ExpectedFormatForExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => FormatId.Jpeg,
            ".png"                                  => FormatId.Png,
            ".gif"                                  => FormatId.Gif,
            ".bmp" or ".dib"                        => FormatId.Bmp,
            ".tif" or ".tiff"                       => FormatId.Tiff,
            ".webp"                                 => FormatId.WebP,
            ".heic" or ".heif"                      => FormatId.Heif,
            ".avif"                                 => FormatId.Avif,
            _                                       => null
        };
}

// Identifies a detected image format family.
public enum FormatId
{
    Jpeg,
    Png,
    Gif,
    Bmp,
    Tiff,
    WebP,
    Heif,
    Avif,
    IsoBmff
}
