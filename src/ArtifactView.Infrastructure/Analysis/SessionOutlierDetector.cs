namespace ArtifactView.Infrastructure.Analysis;

// Detects files that are anomalous relative to the rest of the session.
// Runs as a folder-level pass after per-file enrichment.
//
// Two checks:
//   Timestamp outlier — capture time falls outside Tukey's fences on the
//     session's timestamp distribution (Q1 - 1.5*IQR or Q3 + 1.5*IQR),
//     and at least MinOutlierGap beyond the nearest quartile.
//   Camera model outlier — file uses a different camera make+model than
//     the session majority (≥ MajorityThreshold), and there are enough
//     samples to make the claim credible.
public static class SessionOutlierDetector
{
    private const int    MinTimestampSamples = 4;
    private const int    MinModelSamples     = 3;
    private const double MajorityThreshold   = 0.70;
    // Floor on the outlier gap so a 5-minute IQR doesn't flag photos 10 min apart.
    private static readonly TimeSpan MinOutlierGap = TimeSpan.FromHours(2);

    public sealed record OutlierResult(string Path, IReadOnlyList<string> Reasons);

    public static IReadOnlyList<OutlierResult> Detect(
        IReadOnlyList<(string Path, DateTime? CaptureTime, string? CameraModel)> items)
    {
        var reasonMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        DetectTimestampOutliers(items, reasonMap);
        DetectCameraModelOutliers(items, reasonMap);

        return reasonMap
            .Select(kvp => new OutlierResult(kvp.Key, kvp.Value))
            .ToList();
    }

    private static void DetectTimestampOutliers(
        IReadOnlyList<(string Path, DateTime? CaptureTime, string? CameraModel)> items,
        Dictionary<string, List<string>> reasonMap)
    {
        var withTimes = items
            .Where(i => i.CaptureTime.HasValue)
            .OrderBy(i => i.CaptureTime!.Value)
            .ToList();

        if (withTimes.Count < MinTimestampSamples)
            return;

        var ticks = withTimes.Select(i => (double)i.CaptureTime!.Value.Ticks).ToArray();
        var q1    = Percentile(ticks, 0.25);
        var q3    = Percentile(ticks, 0.75);
        var iqr   = q3 - q1;

        // With a very tight IQR (e.g. burst photos), enforce a minimum gap floor.
        var floor = MinOutlierGap.Ticks;
        var lower = q1 - Math.Max(1.5 * iqr, floor);
        var upper = q3 + Math.Max(1.5 * iqr, floor);

        foreach (var (path, captureTime, _) in withTimes)
        {
            var t = (double)captureTime!.Value.Ticks;
            if (t < lower || t > upper)
            {
                var dist = t < lower
                    ? TimeSpan.FromTicks((long)(q1 - t))
                    : TimeSpan.FromTicks((long)(t - q3));
                AddReason(reasonMap, path,
                    $"Capture time is {FormatSpan(dist)} outside the session's timestamp range.");
            }
        }
    }

    private static void DetectCameraModelOutliers(
        IReadOnlyList<(string Path, DateTime? CaptureTime, string? CameraModel)> items,
        Dictionary<string, List<string>> reasonMap)
    {
        var withModels = items
            .Where(i => !string.IsNullOrWhiteSpace(i.CameraModel))
            .ToList();

        if (withModels.Count < MinModelSamples)
            return;

        var groups = withModels
            .GroupBy(i => i.CameraModel!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        var dominant      = groups[0];
        var dominantRatio = (double)dominant.Count() / withModels.Count;

        if (dominantRatio < MajorityThreshold)
            return;

        foreach (var group in groups.Skip(1))
        {
            foreach (var (path, _, cameraModel) in group)
            {
                AddReason(reasonMap, path,
                    $"Camera model '{cameraModel}' differs from session majority " +
                    $"({dominant.Key}, {dominant.Count()} of {withModels.Count} files).");
            }
        }
    }

    private static void AddReason(
        Dictionary<string, List<string>> map, string path, string reason)
    {
        if (!map.TryGetValue(path, out var list))
        {
            list     = [];
            map[path] = list;
        }
        list.Add(reason);
    }

    // Returns the value at the given percentile (0.0–1.0) of a sorted array.
    private static double Percentile(double[] sortedValues, double p)
    {
        if (sortedValues.Length == 0)
            return 0;
        var idx = p * (sortedValues.Length - 1);
        var lo  = (int)Math.Floor(idx);
        var hi  = (int)Math.Ceiling(idx);
        if (lo == hi)
            return sortedValues[lo];
        var frac = idx - lo;
        return sortedValues[lo] * (1 - frac) + sortedValues[hi] * frac;
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1.0)
            return $"{span.TotalDays:F1} day(s)";
        if (span.TotalHours >= 1.0)
            return $"{span.TotalHours:F1} hour(s)";
        return $"{span.TotalMinutes:F0} minute(s)";
    }
}
