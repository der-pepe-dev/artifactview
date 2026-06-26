using ArtifactView.Core.Models;

namespace ArtifactView.Infrastructure.Analysis;

// Cross-checks the EXIF-GPS timestamp offset against the timezone implied by
// the GPS capture coordinates.
//
// Strategy: UTC offset ≈ longitude ÷ 15 (each 15° = 1 hour), rounded to the
// nearest half-hour to accommodate non-integer zones (India +5:30, Nepal +5:45,
// etc.).  The result is a rough estimate — political timezone boundaries can
// deviate ±2–3 h from solar longitude.  A ±3 h tolerance is therefore applied
// before raising an anomaly.
//
// Only longitude is used; latitude has no bearing on UTC offset.
// Only runs when all three inputs are available: longitude, EXIF local time,
// GPS UTC time.
public static class TimezoneInferenceAnalyzer
{
    // Political timezone boundaries deviate up to ±2 h from solar longitude
    // in extreme cases (e.g. western China using UTC+8 despite being at UTC+6
    // solar).  Allow 3 h to also absorb ±1 h DST.
    private const double ToleranceHours = 3.0;

    public static IReadOnlyList<Finding> Analyze(
        double? longitudeDeg,
        DateTime? captureDateLocal,
        DateTime? gpsDateTimeUtc)
    {
        if (longitudeDeg is null || captureDateLocal is null || gpsDateTimeUtc is null)
            return [];

        var exif = captureDateLocal.Value;
        var gps  = gpsDateTimeUtc.Value;

        // Solar UTC offset rounded to nearest 0.5 h.
        var estimatedOffset = Math.Round(longitudeDeg.Value / 15.0 * 2.0) / 2.0;
        var actualDiff      = (exif - gps).TotalHours;
        var deviation       = Math.Abs(actualDiff - estimatedOffset);

        var tzLabel = estimatedOffset == 0
            ? "UTC+0"
            : $"UTC{estimatedOffset:+0.#;-0.#}";

        var results = new List<Finding>();

        if (deviation <= ToleranceHours)
        {
            results.Add(new Finding
            {
                Id       = "timezone-inference-consistent",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.None,
                Observation =
                    $"Camera offset from GPS UTC ({actualDiff:+0.#;-0.#;0} h) is within " +
                    $"{deviation:F1} h of the solar timezone for the capture longitude " +
                    $"({longitudeDeg.Value:+0.###;-0.###}° → {tzLabel}).",
                ObservationConfidence    = new ConfidenceScore(70),
                Interpretation =
                    "GPS coordinates and EXIF timestamp are mutually consistent. " +
                    "The offset is plausibly explained by the local timezone.",
                InterpretationConfidence = new ConfidenceScore(60)
            });
        }
        else
        {
            results.Add(new Finding
            {
                Id       = "timezone-inference-offset-mismatch",
                Category = "Timestamps",
                ReviewPriority = ReviewPriority.Medium,
                Observation =
                    $"Camera offset from GPS UTC ({actualDiff:+0.#;-0.#;0} h) deviates " +
                    $"{deviation:F1} h from the solar timezone for the capture longitude " +
                    $"({longitudeDeg.Value:+0.###;-0.###}° → {tzLabel}).",
                ObservationConfidence    = new ConfidenceScore(70),
                Interpretation =
                    "Possible causes: GPS fix is from a different location than where the " +
                    "photo was taken (e.g. cached or transplanted GPS data), camera clock " +
                    "not updated after cross-timezone travel, or a country-specific UTC " +
                    "offset that diverges significantly from its solar longitude.",
                InterpretationConfidence = new ConfidenceScore(50),
                SupportingFactors =
                [
                    $"Longitude {longitudeDeg.Value:F3}° implies approx. {tzLabel}",
                    $"Actual EXIF–GPS offset: {actualDiff:+0.##;-0.##;0} h",
                    $"Deviation from expected: {deviation:F1} h"
                ]
            });
        }

        return results;
    }
}
