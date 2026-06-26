using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtifactView.Core.Models;

namespace ArtifactView.App.Viewing;

// Compares the pixel content of an embedded EXIF thumbnail against a scaled-down
// version of the main image.
//
// Both images are normalised to Pbgra32 and scaled to a 64×64 tile for the
// comparison. Scaling averages out JPEG block artefacts, so the resulting
// Mean Absolute Difference (MAD) reflects genuine content differences rather
// than compression noise.
//
// MAD thresholds (empirical, JPEG-specific):
//   <  12  — consistent: normal variation from re-encoding at any quality
//   12-35  — mild difference: possible minor edit, colour grade, or re-save
//   > 35   — significant: thumbnail likely shows a different or earlier image
//
// These values assume a 64×64 comparison tile in Pbgra32.  Revise with real
// samples if the forensic context demands tighter bounds.
internal static class ThumbnailContentAnalyzer
{
    private const int CompareSize = 64;

    /// <returns>
    /// A <see cref="Finding"/> describing the pixel similarity, or <c>null</c>
    /// if comparison could not be performed (e.g. an unsupported pixel format).
    /// </returns>
    public static Finding? Analyze(BitmapSource thumbnail, BitmapSource mainImage)
    {
        if (thumbnail.PixelWidth  == 0 || thumbnail.PixelHeight  == 0 ||
            mainImage.PixelWidth  == 0 || mainImage.PixelHeight  == 0)
            return null;

        try
        {
            var thumbPixels = GetResizedPixels(thumbnail, CompareSize, CompareSize);
            var mainPixels  = GetResizedPixels(mainImage,  CompareSize, CompareSize);

            double totalDiff = 0;
            int    n         = Math.Min(thumbPixels.Length, mainPixels.Length);
            for (var i = 0; i < n; i++)
                totalDiff += Math.Abs(thumbPixels[i] - mainPixels[i]);

            var mad = totalDiff / n;
            return BuildFinding(mad);
        }
        catch
        {
            // Non-fatal: structural and dimension findings still stand.
            return null;
        }
    }

    // Converts src to Pbgra32 and scales it to (targetW × targetH).
    // TransformedBitmap.CopyPixels materialises the transform on the fly —
    // no intermediate WriteableBitmap is needed.
    private static byte[] GetResizedPixels(BitmapSource src, int targetW, int targetH)
    {
        BitmapSource normalised = src.Format == PixelFormats.Pbgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);

        var scaled = new TransformedBitmap(
            normalised,
            new ScaleTransform(
                (double)targetW / normalised.PixelWidth,
                (double)targetH / normalised.PixelHeight));

        var stride = targetW * 4; // 4 bytes per pixel (Pbgra32)
        var pixels = new byte[stride * targetH];
        scaled.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static Finding BuildFinding(double mad) => mad switch
    {
        < 12 => new Finding
        {
            Id = "thumb-pixel-consistent",
            Category = "Thumbnail",
            ReviewPriority = ReviewPriority.None,
            Observation =
                $"Thumbnail pixel content is consistent with the main image (MAD \u00d7 64: {mad:F1}).",
            ObservationConfidence = new ConfidenceScore(75),
            Interpretation =
                "Difference level is within the range expected from JPEG re-encoding.",
            InterpretationConfidence = new ConfidenceScore(70)
        },
        < 35 => new Finding
        {
            Id = "thumb-pixel-mild-diff",
            Category = "Thumbnail",
            ReviewPriority = ReviewPriority.Low,
            Observation =
                $"Thumbnail pixel content shows mild differences from the main image (MAD \u00d7 64: {mad:F1}).",
            ObservationConfidence = new ConfidenceScore(82),
            Interpretation =
                "Consistent with colour grading, brightness/contrast adjustment, or re-encoding " +
                "after the thumbnail was written. The thumbnail may represent an earlier edit state.",
            InterpretationConfidence = new ConfidenceScore(60)
        },
        _ => new Finding
        {
            Id = "thumb-pixel-significant-diff",
            Category = "Thumbnail",
            ReviewPriority = ReviewPriority.Medium,
            Observation =
                $"Thumbnail pixel content differs significantly from the main image (MAD \u00d7 64: {mad:F1}).",
            ObservationConfidence = new ConfidenceScore(88),
            Interpretation =
                "Consistent with the main image being edited, cropped, or replaced after the " +
                "original thumbnail was written. The thumbnail may represent an earlier version.",
            InterpretationConfidence = new ConfidenceScore(65)
        }
    };
}
