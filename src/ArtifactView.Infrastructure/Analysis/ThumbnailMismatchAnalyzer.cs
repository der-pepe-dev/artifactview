using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Compares the embedded EXIF thumbnail dimensions against the main image
// dimensions to surface anomalies consistent with thumbnail substitution,
// cropping, or metadata transplant.
//
// All findings use confidence-based language.  A mismatch is not proof of
// tampering — it can occur legitimately (e.g., camera stores a fixed-size
// thumbnail, Lightroom exports with a different aspect ratio, etc.).
public static class ThumbnailMismatchAnalyzer
{
    public static IReadOnlyList<Finding> Analyze(
        int? mainWidth, int? mainHeight,
        int? thumbWidth, int? thumbHeight)
    {
        if (mainWidth is null || mainHeight is null) return [];
        if (thumbWidth is null || thumbHeight is null) return [];
        if (thumbWidth <= 0 || thumbHeight <= 0) return [];
        if (mainWidth  <= 0 || mainHeight  <= 0) return [];

        var results = new List<Finding>();

        // ── 1. Thumbnail larger than main image ───────────────────────────────
        // Thumbnails are always smaller than or equal to the main image in a
        // legitimate camera file. A larger thumbnail is a strong anomaly.
        if (thumbWidth > mainWidth || thumbHeight > mainHeight)
        {
            results.Add(new Finding
            {
                Id       = "thumb-larger-than-main",
                Category = "Embedded Thumbnail",
                ReviewPriority = ReviewPriority.High,
                Observation =
                    $"Embedded thumbnail ({thumbWidth}×{thumbHeight}) is larger than " +
                    $"the main image ({mainWidth}×{mainHeight}).",
                ObservationConfidence = new ConfidenceScore(97),
                Interpretation =
                    "Unusual. Consistent with the thumbnail being sourced from a different, " +
                    "higher-resolution image, or the main image having been resized after the " +
                    "EXIF thumbnail was written. Review recommended.",
                InterpretationConfidence = new ConfidenceScore(75)
            });
            return results; // larger thumb overrides all other checks
        }

        // ── 2. Aspect ratio mismatch ──────────────────────────────────────────
        // Compute aspect ratios and compare. Allow a small tolerance for
        // integer rounding artefacts in stored dimensions.
        var mainAr  = (double)mainWidth.Value  / mainHeight.Value;
        var thumbAr = (double)thumbWidth.Value / thumbHeight.Value;
        var arDiff  = Math.Abs(mainAr - thumbAr);

        // 5 % tolerance absorbs rounding in stored dimension values.
        const double ArTolerance = 0.05;

        if (arDiff > ArTolerance)
        {
            // Distinguish portrait/landscape flip from genuine aspect difference.
            var inverseAr   = 1.0 / mainAr;
            var inverseArDiff = Math.Abs(inverseAr - thumbAr);

            if (inverseArDiff < ArTolerance)
            {
                results.Add(new Finding
                {
                    Id       = "thumb-orientation-mismatch",
                    Category = "Embedded Thumbnail",
                    ReviewPriority = ReviewPriority.Low,
                    Observation =
                        $"Embedded thumbnail aspect ratio ({thumbWidth}×{thumbHeight}) " +
                        $"is the transpose of the main image aspect ratio ({mainWidth}×{mainHeight}).",
                    ObservationConfidence = new ConfidenceScore(90),
                    Interpretation =
                        "Consistent with the thumbnail being stored in a different orientation " +
                        "than the main image. Possible EXIF Orientation tag rotation applied " +
                        "to one but not the other.",
                    InterpretationConfidence = new ConfidenceScore(65)
                });
            }
            else
            {
                results.Add(new Finding
                {
                    Id       = "thumb-aspect-ratio-mismatch",
                    Category = "Embedded Thumbnail",
                    ReviewPriority = ReviewPriority.Medium,
                    Observation =
                        $"Embedded thumbnail aspect ratio ({thumbWidth}×{thumbHeight} → " +
                        $"{thumbAr:F3}) differs from main image ({mainWidth}×{mainHeight} → " +
                        $"{mainAr:F3}) by {arDiff:P1}.",
                    ObservationConfidence = new ConfidenceScore(93),
                    Interpretation =
                        "Consistent with the main image being cropped after capture without " +
                        "updating the EXIF thumbnail, or the thumbnail sourced from a " +
                        "different framing of the same scene.",
                    InterpretationConfidence = new ConfidenceScore(65)
                });
            }
        }
        else
        {
            // Dimensions consistent — report as positive confirmation.
            results.Add(new Finding
            {
                Id       = "thumb-dimensions-consistent",
                Category = "Embedded Thumbnail",
                ReviewPriority = ReviewPriority.None,
                Observation =
                    $"Embedded thumbnail dimensions ({thumbWidth}×{thumbHeight}) are " +
                    $"proportionally consistent with the main image ({mainWidth}×{mainHeight}).",
                ObservationConfidence = new ConfidenceScore(92)
            });
        }

        return results;
    }
}
