using System.Drawing;
using System.Drawing.Imaging;

namespace ArtifactView.Infrastructure.Reconstruction;

// Decodes a cached thumbnail payload (JPEG, BMP, or other) and re-encodes it
// as a lossless PNG.  The result is a lo-fi reconstruction — it faithfully
// represents the cached thumbnail at its original resolution but is NOT the
// source file.
public static class LoFiReconstructionExporter
{
    public sealed record Result(
        byte[] PngBytes,
        int    Width,
        int    Height);

    // Returns null when the input cannot be decoded as an image.
    public static Result? Export(byte[] thumbnailBytes)
    {
        if (thumbnailBytes is not { Length: > 0 })
            return null;

        try
        {
            using var inMs = new MemoryStream(thumbnailBytes, writable: false);
            using var bmp  = new Bitmap(inMs);

            using var outMs = new MemoryStream();
            bmp.Save(outMs, ImageFormat.Png);
            return new Result(outMs.ToArray(), bmp.Width, bmp.Height);
        }
        catch
        {
            return null;
        }
    }
}
