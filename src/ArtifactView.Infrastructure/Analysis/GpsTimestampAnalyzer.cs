using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Cross-checks the GPS UTC timestamp against the EXIF capture timestamp.
//
// Key constraint: EXIF DateTimeOriginal is a naive local datetime — it stores
// whatever the camera clock shows, with no timezone offset. GPS time is always
// UTC. Direct subtraction is therefore only valid when the camera timezone is
// known, which it usually isn't.
//
// Strategy:
//  - Compare date components independently of time when the difference is large.
//  - For same-date captures, compare time components allowing up to 26 hours of
//    timezone + DST tolerance (cameras can be 14 hours ahead of UTC or behind).
//  - Only raise anomalies when the gap exceeds what any timezone can explain.
public static class GpsTimestampAnalyzer
{
    // Maximum timezone offset is UTC+14 (Line Islands). Use 26 h to also
    // absorb 1-hour DST uncertainty and minor clock drift.
    private static readonly TimeSpan s_maxTzOffset = TimeSpan.FromHours(26);

    public static IReadOnlyList<Finding> Analyze(DateTime? captureDateLocal, DateTime? gpsDateTimeUtc)
    {
        if (captureDateLocal is null || gpsDateTimeUtc is null)
            return [];

        var exif = captureDateLocal.Value;
        var gps  = gpsDateTimeUtc.Value;

        var results = new List<Finding>();

        // Raw difference — meaningless without timezone, but its magnitude is informative.
        var rawDiff = exif - gps;

        // ── 1. Same-date / small-diff path ────────────────────────────────────
        if (Math.Abs(rawDiff.TotalHours) <= s_maxTzOffset.TotalHours)
        {
            results.Add(new Finding
            {
                Id       = "gps-ts-consistent",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.None,
                Observation =
                    $"GPS UTC timestamp ({gps:yyyy-MM-dd HH:mm:ss} UTC) and EXIF " +
                    $"DateTimeOriginal ({exif:yyyy-MM-dd HH:mm:ss}) differ by " +
                    $"{rawDiff.TotalHours:+#.#;-#.#;0.0} h — within timezone tolerance.",
                ObservationConfidence = new ConfidenceScore(85),
                Interpretation =
                    "Consistent with correct GPS-synchronized capture. The difference is " +
                    "explained by the camera being set to a local timezone."
            });
            return results;
        }

        // ── 2. Multi-day gap ──────────────────────────────────────────────────
        var absDays = Math.Abs(rawDiff.TotalDays);

        if (absDays >= 1.0)
        {
            var direction = rawDiff.TotalHours > 0 ? "ahead of" : "behind";
            results.Add(new Finding
            {
                Id       = "gps-ts-date-mismatch",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.High,
                Observation =
                    $"EXIF DateTimeOriginal ({exif:yyyy-MM-dd HH:mm}) is {absDays:F0} day(s) " +
                    $"{direction} GPS UTC timestamp ({gps:yyyy-MM-dd HH:mm} UTC).",
                ObservationConfidence = new ConfidenceScore(90),
                Interpretation =
                    "Cannot be explained by timezone offset alone. Possible causes: " +
                    "camera date set incorrectly, metadata rewrite after capture with " +
                    "incorrect date, GPS timestamp from a different acquisition, " +
                    "or GPS data transplanted from another file.",
                InterpretationConfidence = new ConfidenceScore(65),
                SupportingFactors = [$"Raw offset: {rawDiff.TotalHours:+0.#;-0.#} hours"]
            });
            return results;
        }

        // ── 3. Same day but > 26 h time component gap (should be impossible) ─
        // Guard for edge cases (sub-day files, multi-day wrap, etc.)
        results.Add(new Finding
        {
            Id       = "gps-ts-time-anomaly",
            Category = "Timestamps",
            ReviewPriority = ReviewPriority.Medium,
            Observation =
                $"GPS UTC timestamp ({gps:yyyy-MM-dd HH:mm:ss} UTC) and EXIF " +
                $"DateTimeOriginal ({exif:yyyy-MM-dd HH:mm:ss}) differ by " +
                $"{rawDiff.TotalHours:+#.#;-#.#} h — outside the expected timezone range.",
            ObservationConfidence = new ConfidenceScore(85),
            Interpretation =
                "Possible timezone misconfiguration on the camera, DST boundary capture, " +
                "or GPS data from a different acquisition context.",
            InterpretationConfidence = new ConfidenceScore(55)
        });

        return results;
    }
}
