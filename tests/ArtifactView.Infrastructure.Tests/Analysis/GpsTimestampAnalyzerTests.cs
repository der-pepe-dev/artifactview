using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class GpsTimestampAnalyzerTests
{
    private static DateTime Exif(int year, int month, int day, int h, int m, int s) =>
        new(year, month, day, h, m, s, DateTimeKind.Unspecified);

    private static DateTime GpsUtc(int year, int month, int day, int h, int m, int s) =>
        new(year, month, day, h, m, s, DateTimeKind.Utc);

    [Test]
    public async Task Returns_empty_when_capture_date_null()
    {
        var results = GpsTimestampAnalyzer.Analyze(null, GpsUtc(2024, 6, 1, 10, 0, 0));
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Returns_empty_when_gps_date_null()
    {
        var results = GpsTimestampAnalyzer.Analyze(Exif(2024, 6, 1, 10, 0, 0), null);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Consistent_when_within_timezone_range()
    {
        // EXIF 12:00 local, GPS 10:00 UTC — 2h offset (Europe/Paris) — consistent
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 1, 12, 0, 0),
            GpsUtc(2024, 6, 1, 10, 0, 0));
        await Assert.That(results).Contains(r => r.Id == "gps-ts-consistent");
    }

    [Test]
    public async Task Consistent_for_large_but_valid_timezone_offset()
    {
        // EXIF UTC+14 (Line Islands) — GPS UTC, same moment
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 2, 0, 0, 0),  // local = UTC+14, midnight on day 2
            GpsUtc(2024, 6, 1, 10, 0, 0)); // UTC day 1, 10:00 — diff = +14h
        await Assert.That(results).Contains(r => r.Id == "gps-ts-consistent");
    }

    [Test]
    public async Task Date_mismatch_for_multi_day_gap()
    {
        // 3-day gap — can't be timezone
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 5, 12, 0, 0),
            GpsUtc(2024, 6, 1, 12, 0, 0));
        var match = await Assert.That(results).HasSingleItem();
        await Assert.That(match.ReviewPriority).IsEqualTo(ReviewPriority.High);
    }

    [Test]
    public async Task Date_mismatch_includes_day_count_in_observation()
    {
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 10, 12, 0, 0),
            GpsUtc(2024, 6, 1, 12, 0, 0));
        var match = results.First(r => r.Id == "gps-ts-date-mismatch");
        await Assert.That(match.Observation).Contains("9"); // 9-day gap
    }

    [Test]
    public async Task Date_mismatch_confidence_is_high()
    {
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 10, 12, 0, 0),
            GpsUtc(2024, 6, 1, 12, 0, 0));
        var match = results.First(r => r.Id == "gps-ts-date-mismatch");
        await Assert.That(match.ObservationConfidence.Value >= 85).IsTrue();
    }

    [Test]
    public async Task Consistent_finding_mentions_timezone()
    {
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 1, 18, 0, 0),
            GpsUtc(2024, 6, 1, 10, 0, 0)); // 8h offset — UTC+8
        var match = results.First(r => r.Id == "gps-ts-consistent");
        await Assert.That(match.Interpretation).Contains("timezone");
    }

    [Test]
    public async Task Exact_match_is_consistent()
    {
        var results = GpsTimestampAnalyzer.Analyze(
            Exif(2024, 6, 1, 10, 0, 0),
            GpsUtc(2024, 6, 1, 10, 0, 0));
        await Assert.That(results).Contains(r => r.Id == "gps-ts-consistent");
    }
}