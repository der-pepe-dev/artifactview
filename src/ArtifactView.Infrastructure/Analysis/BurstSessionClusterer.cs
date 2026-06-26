namespace ArtifactView.Infrastructure.Analysis;

// Groups photos by temporal proximity into bursts and sessions.
//
// Burst  — consecutive shots with < BurstThreshold gap (default 5 s).
//          Typical: multiple frames from rapid-fire / continuous drive mode.
//
// Session — consecutive shots with < SessionThreshold gap (default 30 min).
//           Typical: photos taken during the same outing or event.
//
// Files without capture timestamps are assigned cluster ID 0 (unclustered).
// IDs are ordinal within a folder load; they reset on every new folder.
public static class BurstSessionClusterer
{
    public static readonly TimeSpan BurstThreshold   = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SessionThreshold = TimeSpan.FromMinutes(30);

    /// <param name="items">
    /// (path, captureTime) pairs — items with null captureTime are assigned
    /// BurstId=0 and SessionId=0.
    /// </param>
    public static IReadOnlyList<ClusterAssignment> Cluster(
        IEnumerable<(string Path, DateTime? CaptureTime)> items)
    {
        // Sort by capture time (nulls last), then assign clusters in one pass.
        var sorted = items
            .Select(x => (x.Path, x.CaptureTime))
            .OrderBy(x => x.CaptureTime ?? DateTime.MaxValue)
            .ToList();

        var results = new List<ClusterAssignment>(sorted.Count);

        int burstId   = 0;
        int sessionId = 0;
        DateTime? prevTime = null;

        foreach (var (path, captureTime) in sorted)
        {
            if (captureTime is null)
            {
                results.Add(new ClusterAssignment(path, BurstId: 0, SessionId: 0));
                prevTime = null;
                continue;
            }

            if (prevTime is null)
            {
                // First timed item — start new burst and session.
                burstId++;
                sessionId++;
            }
            else
            {
                var gap = captureTime.Value - prevTime.Value;

                if (gap > SessionThreshold)
                {
                    burstId++;
                    sessionId++;
                }
                else if (gap > BurstThreshold)
                {
                    // New burst within the same session.
                    burstId++;
                }
                // else: same burst and session as previous item.
            }

            results.Add(new ClusterAssignment(path, BurstId: burstId, SessionId: sessionId));
            prevTime = captureTime.Value;
        }

        return results;
    }
}

/// <summary>Cluster membership for one file.</summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="BurstId">
/// Ordinal burst group within the folder.  0 = no timestamp / unclustered.
/// Files with the same non-zero BurstId were shot within 5 seconds of each other.
/// </param>
/// <param name="SessionId">
/// Ordinal session group within the folder.  0 = unclustered.
/// Files with the same non-zero SessionId were shot within 30 minutes of each other.
/// </param>
public sealed record ClusterAssignment(string Path, int BurstId, int SessionId);
