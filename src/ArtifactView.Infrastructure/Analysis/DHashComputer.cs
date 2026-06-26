using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ArtifactView.Infrastructure.Analysis;

// Computes dHash from an image file using System.Drawing.
// Returns null when the file cannot be decoded (unsupported format, corrupt, etc.).
public static class DHashComputer
{
    public static PerceptualHash? ComputeFromFile(string path)
    {
        try
        {
            using var original = Image.FromFile(path);
            return ComputeFromImage(original);
        }
        catch
        {
            return null;
        }
    }

    public static PerceptualHash? ComputeFromBytes(byte[] imageBytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(imageBytes);
            using var img = Image.FromStream(ms);
            return ComputeFromImage(img);
        }
        catch
        {
            return null;
        }
    }

    private static PerceptualHash ComputeFromImage(Image source)
    {
        // Scale to 9×8 grayscale.
        using var scaled = new Bitmap(9, 8, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode  = InterpolationMode.HighQualityBilinear;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(source, 0, 0, 9, 8);
        }

        Span<byte> pixels = stackalloc byte[72]; // 9 × 8

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                var c = scaled.GetPixel(col, row);
                // ITU-R BT.601 luma coefficients.
                pixels[row * 9 + col] = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
            }
        }

        return PerceptualHash.FromGrayscale9x8(pixels);
    }
}
