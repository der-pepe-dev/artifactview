using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class BurstSessionClustererTests
{
    private static DateTime T(int secondsFromBase) =>
        new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc).AddSeconds(secondsFromBase);

    private static (string Path, DateTime? CaptureTime) I(string name, int secs)
        => ($"/folder/{name}", T(secs));

    private static (string Path, DateTime? CaptureTime) NoTs(string name)
        => ($"/folder/{name}", null);

    [Fact]
    public void Returns_empty_for_no_inputs()
        => Assert.Empty(BurstSessionClusterer.Cluster([]));

    [Fact]
    public void Single_file_gets_session_id_one()
    {
        var result = BurstSessionClusterer.Cluster([I("a.jpg", 0)]);
        var item   = Assert.Single(result);
        Assert.Equal(1, item.SessionId);
    }

    [Fact]
    public void Files_without_timestamps_get_id_zero()
    {
        var result = BurstSessionClusterer.Cluster([NoTs("a.jpg"), NoTs("b.jpg")]);
        Assert.All(result, r => Assert.Equal(0, r.BurstId));
        Assert.All(result, r => Assert.Equal(0, r.SessionId));
    }

    [Fact]
    public void Rapid_shots_share_burst_id()
    {
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 1),
            I("c.jpg", 2),
        ]);
        var burstIds = result.Select(r => r.BurstId).Distinct().ToList();
        Assert.Single(burstIds); // all same burst
        Assert.NotEqual(0, burstIds[0]);
    }

    [Fact]
    public void Gap_beyond_burst_threshold_starts_new_burst()
    {
        // 0s and 10s apart — 10s > 5s burst threshold.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 10),
        ]);
        Assert.NotEqual(result[0].BurstId, result[1].BurstId);
    }

    [Fact]
    public void Gap_within_session_threshold_keeps_same_session()
    {
        // 10s gap — within 30-min session threshold.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 10),
        ]);
        Assert.Equal(result[0].SessionId, result[1].SessionId);
    }

    [Fact]
    public void Gap_beyond_session_threshold_starts_new_session()
    {
        // 31 minutes gap.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 31 * 60),
        ]);
        Assert.NotEqual(result[0].SessionId, result[1].SessionId);
    }

    [Fact]
    public void Two_bursts_in_same_session_have_same_session_id()
    {
        // Burst 1: 0–2s, pause 10s, Burst 2: 12–14s — all within session window.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 1),
            I("c.jpg", 12),
            I("d.jpg", 13),
        ]);
        // a,b are burst 1; c,d are burst 2; all session 1
        Assert.Equal(result[0].SessionId, result[2].SessionId);
        Assert.NotEqual(result[0].BurstId, result[2].BurstId);
    }

    [Fact]
    public void Result_count_matches_input_count()
    {
        var inputs = Enumerable.Range(0, 10)
            .Select(i => I($"img_{i}.jpg", i * 2));
        var result = BurstSessionClusterer.Cluster(inputs);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Paths_preserved_in_results()
    {
        var result = BurstSessionClusterer.Cluster([I("photo.jpg", 0)]);
        Assert.Equal("/folder/photo.jpg", result[0].Path);
    }

    [Fact]
    public void Unordered_input_clustered_correctly()
    {
        // Provide shots out of chronological order — clusterer must sort by time.
        // a=0s, c=2s should be same burst; b=3600s is a new session.
        var result = BurstSessionClusterer.Cluster([
            I("c.jpg", 2),
            I("b.jpg", 3600),
            I("a.jpg", 0),
        ]);
        // All three have a path; find by path.
        var ra = result.First(r => r.Path.EndsWith("a.jpg"));
        var rc = result.First(r => r.Path.EndsWith("c.jpg"));
        var rb = result.First(r => r.Path.EndsWith("b.jpg"));

        Assert.Equal(ra.BurstId, rc.BurstId);   // a and c are the same burst
        Assert.NotEqual(ra.SessionId, rb.SessionId); // b is a different session
    }
}
