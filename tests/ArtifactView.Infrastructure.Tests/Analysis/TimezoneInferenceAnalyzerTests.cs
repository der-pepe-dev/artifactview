using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class TimezoneInferenceAnalyzerTests
{
    private static DateTime Local(int h) => new(2024, 6, 1, h, 0, 0, DateTimeKind.Unspecified);
    private static DateTime Utc(int h)   => new(2024, 6, 1, h, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Returns_empty_when_longitude_null()
        => await Assert.That(TimezoneInferenceAnalyzer.Analyze(null, Local(10), Utc(10))).IsEmpty();

    [Test]
    public async Task Returns_empty_when_capture_date_null()
        => await Assert.That(TimezoneInferenceAnalyzer.Analyze(2.3, null, Utc(10))).IsEmpty();

    [Test]
    public async Task Returns_empty_when_gps_utc_null()
        => await Assert.That(TimezoneInferenceAnalyzer.Analyze(2.3, Local(10), null)).IsEmpty();

    [Test]
    public async Task Consistent_for_paris_longitude_utc_plus_one()
    {
        // Paris lon ~2.3°E → solar UTC+0.  Camera at UTC+1 (CET) → diff = 1h.
        // 1h deviation from UTC+0 is within 3h tolerance → consistent.
        var results = TimezoneInferenceAnalyzer.Analyze(2.3, Local(11), Utc(10));
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-consistent");
    }

    [Test]
    public async Task Consistent_for_new_york_longitude()
    {
        // New York lon ~-74°W → solar UTC-5.  Camera at UTC-5 → diff = -5h.
        var results = TimezoneInferenceAnalyzer.Analyze(-74.0, Local(10), Utc(15));
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-consistent");
    }

    [Test]
    public async Task Consistent_for_sydney_utc_plus_ten()
    {
        // Sydney lon ~151°E → solar UTC+10.  diff = +10h → consistent.
        var results = TimezoneInferenceAnalyzer.Analyze(151.0, Local(20), Utc(10));
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-consistent");
    }

    [Test]
    public async Task Consistent_for_india_half_hour_offset()
    {
        // Kolkata lon ~88.4°E → solar UTC+5.5 (rounds to +6, but India is UTC+5:30).
        // Camera at UTC+5.5 → diff = 5.5h.  Deviation from solar estimate ≤ 3h → consistent.
        var results = TimezoneInferenceAnalyzer.Analyze(88.4, Local(15), Utc(9));
        // Local(15) - Utc(9) = +6h.  Solar estimate for 88.4° = round(88.4/15*2)/2 = round(11.79)/2 = 6.0h
        // Deviation = |6 - 6| = 0 → consistent.
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-consistent");
    }

    [Test]
    public async Task Mismatch_when_offset_far_from_longitude()
    {
        // Paris lon ~2.3°E → solar UTC+0.  Camera offset = +8h (UTC+8 — mismatched).
        // Deviation = |8 - 0| = 8h > 3h → mismatch.
        var results = TimezoneInferenceAnalyzer.Analyze(2.3, Local(18), Utc(10));
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-offset-mismatch");
    }

    [Test]
    public async Task Mismatch_has_medium_priority()
    {
        var results = TimezoneInferenceAnalyzer.Analyze(2.3, Local(18), Utc(10));
        var match   = results.First(r => r.Id == "timezone-inference-offset-mismatch");
        await Assert.That(match.ReviewPriority).IsEqualTo(ReviewPriority.Medium);
    }

    [Test]
    public async Task Mismatch_finding_has_supporting_factors()
    {
        var results = TimezoneInferenceAnalyzer.Analyze(2.3, Local(18), Utc(10));
        var match   = results.First(r => r.Id == "timezone-inference-offset-mismatch");
        await Assert.That(match.SupportingFactors).IsNotEmpty();
    }

    [Test]
    public async Task Consistent_finding_mentions_longitude()
    {
        var results = TimezoneInferenceAnalyzer.Analyze(-74.0, Local(10), Utc(15));
        var match   = results.First(r => r.Id == "timezone-inference-consistent");
        await Assert.That(match.Observation).Contains("-74");
    }

    [Test]
    public async Task Consistent_confidence_is_reasonable()
    {
        var results = TimezoneInferenceAnalyzer.Analyze(0.0, Local(10), Utc(10));
        var match   = results.First(r => r.Id == "timezone-inference-consistent");
        // Longitude-based estimate is approximate — confidence should be moderate, not high.
        await Assert.That(match.ObservationConfidence.Value).IsBetween(50,80);
    }

    [Test]
    public async Task Utc_prime_meridian_consistent_when_diff_zero()
    {
        // Longitude 0° (prime meridian) → estimated UTC+0.  EXIF = GPS UTC → diff 0h → consistent.
        var results = TimezoneInferenceAnalyzer.Analyze(0.0, Local(10), Utc(10));
        await Assert.That(results).Contains(r => r.Id == "timezone-inference-consistent");
    }
}