using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Compares the three EXIF timestamp fields against each other and against the
// filesystem last-write time.
//
// Limitation: EXIF stores naive local datetimes with no timezone offset, so
// filesystem UTC cannot be directly compared. A 24-hour tolerance absorbs
// typical timezone differences. Confidence scores reflect this ambiguity.
public static class TimestampConsistencyAnalyzer
{
    private static readonly TimeSpan s_fsTolerance   = TimeSpan.FromHours(24);
    private static readonly TimeSpan s_exifTolerance = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<Finding> Analyze(
        string    path,
        DateTime? captureDate,
        DateTime? dateTimeDigitized = null,
        DateTime? dateTimeModified  = null)
    {
        if (captureDate is null || !File.Exists(path))
            return [];

        var results = new List<Finding>();

        // ── 1. Filesystem last-write vs DateTimeOriginal ──────────────────────
        var fsLocal = File.GetLastWriteTimeUtc(path).ToLocalTime();
        var diff    = fsLocal - captureDate.Value;

        if (Math.Abs(diff.TotalHours) <= s_fsTolerance.TotalHours)
        {
            results.Add(new Finding
            {
                Id = "ts-fs-consistent",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.None,
                Observation =
                    $"Filesystem modification time ({fsLocal:yyyy-MM-dd HH:mm}) is within 24 h of " +
                    $"EXIF DateTimeOriginal ({captureDate.Value:yyyy-MM-dd HH:mm}).",
                ObservationConfidence = new ConfidenceScore(80)
            });
        }
        else if (diff.TotalHours > s_fsTolerance.TotalHours)
        {
            results.Add(new Finding
            {
                Id = "ts-fs-later",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.Low,
                Observation =
                    $"Filesystem modification time ({fsLocal:yyyy-MM-dd}) is {diff.TotalDays:F0} day(s) " +
                    $"after EXIF DateTimeOriginal ({captureDate.Value:yyyy-MM-dd}).",
                ObservationConfidence = new ConfidenceScore(90),
                Interpretation =
                    "Consistent with file transfer, backup, social-media download, or re-save after capture.",
                InterpretationConfidence = new ConfidenceScore(65)
            });
        }
        else
        {
            results.Add(new Finding
            {
                Id = "ts-fs-precedes-exif",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.Medium,
                Observation =
                    $"Filesystem modification time ({fsLocal:yyyy-MM-dd}) appears to precede " +
                    $"EXIF DateTimeOriginal ({captureDate.Value:yyyy-MM-dd}) by {Math.Abs(diff.TotalDays):F0} day(s).",
                ObservationConfidence = new ConfidenceScore(85),
                Interpretation =
                    "Unusual. Possible causes: timezone mismatch, out-of-sync camera clock, " +
                    "or filesystem timestamp manipulation. Verify the capture timezone.",
                InterpretationConfidence = new ConfidenceScore(55)
            });
        }

        // ── 2. IFD0 DateTime (metadata-write) vs DateTimeOriginal ────────────
        // The IFD0 DateTime field is updated by editing tools when they rewrite
        // EXIF. A mismatch indicates the metadata block was modified post-capture.
        if (dateTimeModified.HasValue)
        {
            var modDiff = dateTimeModified.Value - captureDate.Value;

            if (Math.Abs(modDiff.TotalMinutes) <= s_exifTolerance.TotalMinutes)
            {
                results.Add(new Finding
                {
                    Id = "ts-ifd0-consistent",
                    Category = "Timestamps",
                    ReviewPriority = ReviewPriority.None,
                    Observation =
                        $"IFD0 DateTime ({dateTimeModified.Value:yyyy-MM-dd HH:mm}) " +
                        $"matches DateTimeOriginal ({captureDate.Value:yyyy-MM-dd HH:mm}).",
                    ObservationConfidence = new ConfidenceScore(95)
                });
            }
            else
            {
                var days   = Math.Abs(modDiff.TotalDays);
                var detail = days >= 1
                    ? $"{days:F0} day(s)"
                    : $"{Math.Abs(modDiff.TotalHours):F1} hour(s)";

                results.Add(new Finding
                {
                    Id = "ts-ifd0-differs",
                    Category = "Timestamps",
                    ReviewPriority = ReviewPriority.Medium,
                    Observation =
                        $"IFD0 DateTime ({dateTimeModified.Value:yyyy-MM-dd HH:mm}) differs from " +
                        $"DateTimeOriginal ({captureDate.Value:yyyy-MM-dd HH:mm}) by {detail}.",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation =
                        "Consistent with the EXIF metadata block being rewritten after capture — " +
                        "common after editing in Lightroom, Photoshop, exiftool, or similar tools. " +
                        "DateTimeOriginal may still reflect the original capture time.",
                    InterpretationConfidence = new ConfidenceScore(80)
                });
            }
        }

        // ── 3. DateTimeDigitized vs DateTimeOriginal ──────────────────────────
        // These are usually identical for camera-captured images. Divergence
        // can indicate format conversion (e.g., scan → JPEG) or metadata editing.
        if (dateTimeDigitized.HasValue)
        {
            var digDiff = dateTimeDigitized.Value - captureDate.Value;

            if (Math.Abs(digDiff.TotalMinutes) > s_exifTolerance.TotalMinutes)
            {
                results.Add(new Finding
                {
                    Id = "ts-digitized-differs",
                    Category = "Timestamps",
                    ReviewPriority = ReviewPriority.Low,
                    Observation =
                        $"DateTimeDigitized ({dateTimeDigitized.Value:yyyy-MM-dd HH:mm}) differs from " +
                        $"DateTimeOriginal ({captureDate.Value:yyyy-MM-dd HH:mm}).",
                    ObservationConfidence = new ConfidenceScore(95),
                    Interpretation =
                        "Consistent with format conversion (e.g., film scan or non-camera digitization), " +
                        "or independent metadata editing of one field.",
                    InterpretationConfidence = new ConfidenceScore(60)
                });
            }
        }

        return results;
    }
}
