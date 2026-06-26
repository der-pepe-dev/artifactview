using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class BurstSessionClustererTests
{
    private static DateTime T(int secondsFromBase) =>
        new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc).AddSeconds(secondsFromBase);

    private static (string Path, DateTime? CaptureTime) I(string name, int secs)
        => ($"/folder/{name}", T(secs));

    private static (string Path, DateTime? CaptureTime) NoTs(string name)
        => ($"/folder/{name}", null);

    [Test]
    public async Task Returns_empty_for_no_inputs()
        => await Assert.That(BurstSessionClusterer.Cluster([])).IsEmpty();

    [Test]
    public async Task Single_file_gets_session_id_one()
    {
        var result = BurstSessionClusterer.Cluster([I("a.jpg", 0)]);
        var item   = await Assert.That(result).HasSingleItem();
        await Assert.That(item.SessionId).IsEqualTo(1);
    }

    [Test]
    public async Task Files_without_timestamps_get_id_zero()
    {
        var result = BurstSessionClusterer.Cluster([NoTs("a.jpg"), NoTs("b.jpg")]);
        foreach (var r in result)
        {
            await Assert.That(r.BurstId).IsEqualTo(0);
            await Assert.That(r.SessionId).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Rapid_shots_share_burst_id()
    {
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 1),
            I("c.jpg", 2),
        ]);
        var burstIds = result.Select(r => r.BurstId).Distinct().ToList();
        await Assert.That(burstIds).HasSingleItem(); // all same burst
        await Assert.That(burstIds[0]).IsNotEqualTo(0);
    }

    [Test]
    public async Task Gap_beyond_burst_threshold_starts_new_burst()
    {
        // 0s and 10s apart — 10s > 5s burst threshold.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 10),
        ]);
        await Assert.That(result[1].BurstId).IsNotEqualTo(result[0].BurstId);
    }

    [Test]
    public async Task Gap_within_session_threshold_keeps_same_session()
    {
        // 10s gap — within 30-min session threshold.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 10),
        ]);
        await Assert.That(result[1].SessionId).IsEqualTo(result[0].SessionId);
    }

    [Test]
    public async Task Gap_beyond_session_threshold_starts_new_session()
    {
        // 31 minutes gap.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 31 * 60),
        ]);
        await Assert.That(result[1].SessionId).IsNotEqualTo(result[0].SessionId);
    }

    [Test]
    public async Task Two_bursts_in_same_session_have_same_session_id()
    {
        // Burst 1: 0–2s, pause 10s, Burst 2: 12–14s — all within session window.
        var result = BurstSessionClusterer.Cluster([
            I("a.jpg", 0),
            I("b.jpg", 1),
            I("c.jpg", 12),
            I("d.jpg", 13),
        ]);
        // a,b are burst 1; c,d are burst 2; all session 1
        await Assert.That(result[2].SessionId).IsEqualTo(result[0].SessionId);
        await Assert.That(result[2].BurstId).IsNotEqualTo(result[0].BurstId);
    }

    [Test]
    public async Task Result_count_matches_input_count()
    {
        var inputs = Enumerable.Range(0, 10)
            .Select(i => I($"img_{i}.jpg", i * 2));
        var result = BurstSessionClusterer.Cluster(inputs);
        await Assert.That(result.Count).IsEqualTo(10);
    }

    [Test]
    public async Task Paths_preserved_in_results()
    {
        var result = BurstSessionClusterer.Cluster([I("photo.jpg", 0)]);
        await Assert.That(result[0].Path).IsEqualTo("/folder/photo.jpg");
    }

    [Test]
    public async Task Unordered_input_clustered_correctly()
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

        await Assert.That(rc.BurstId).IsEqualTo(ra.BurstId);   // a and c are the same burst
        await Assert.That(rb.SessionId).IsNotEqualTo(ra.SessionId); // b is a different session
    }
}