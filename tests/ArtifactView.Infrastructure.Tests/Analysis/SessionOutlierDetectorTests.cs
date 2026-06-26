using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SessionOutlierDetectorTests
{
    private static readonly DateTime Base = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static (string Path, DateTime? CaptureTime, string? CameraModel) Item(
        string name, DateTime? time = null, string? model = null) =>
        ($"/photos/{name}", time, model);

    [Fact]
    public void Returns_empty_for_single_item()
    {
        var result = SessionOutlierDetector.Detect([Item("a.jpg", Base, "Canon EOS R5")]);
        Assert.Empty(result);
    }

    [Fact]
    public void Returns_empty_when_all_timestamps_similar()
    {
        var items = Enumerable.Range(0, 6)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.Empty(result);
    }

    [Fact]
    public void Detects_timestamp_outlier_far_in_future()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("outlier.jpg", Base.AddDays(30)))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        Assert.Single(result);
        Assert.Equal("/photos/outlier.jpg", result[0].Path);
        Assert.NotEmpty(result[0].Reasons);
        Assert.Contains("outside the session", result[0].Reasons[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detects_timestamp_outlier_far_in_past()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("old.jpg", Base.AddDays(-60)))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var paths = result.Select(r => r.Path).ToList();
        Assert.Contains("/photos/old.jpg", paths);
    }

    [Fact]
    public void Does_not_flag_items_when_fewer_than_min_samples()
    {
        // Only 3 items with timestamps — below MinTimestampSamples=4
        var items = new[]
        {
            Item("a.jpg", Base),
            Item("b.jpg", Base.AddMinutes(5)),
            Item("c.jpg", Base.AddDays(60))   // would be outlier with enough samples
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.Empty(result);
    }

    [Fact]
    public void Ignores_items_without_timestamps()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("no_date.jpg", null))   // no timestamp
            .ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.DoesNotContain(result, r => r.Path.Contains("no_date"));
    }

    [Fact]
    public void Detects_camera_model_outlier()
    {
        var items = Enumerable.Range(0, 7)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .Append(Item("other.jpg", Base.AddMinutes(35), "Samsung Galaxy S24"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var cameraOutlier = result.FirstOrDefault(r => r.Path.Contains("other"));
        Assert.NotNull(cameraOutlier);
        Assert.Contains("Camera model", cameraOutlier!.Reasons[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_flag_camera_outlier_when_no_dominant_majority()
    {
        // 50/50 split — neither is majority
        var items = new[]
        {
            Item("a.jpg", Base, "Canon EOS R5"),
            Item("b.jpg", Base.AddMinutes(5), "Canon EOS R5"),
            Item("c.jpg", Base.AddMinutes(10), "Nikon Z6"),
            Item("d.jpg", Base.AddMinutes(15), "Nikon Z6"),
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.DoesNotContain(result, r => r.Reasons.Any(r2 => r2.Contains("Camera model")));
    }

    [Fact]
    public void Does_not_flag_camera_outlier_with_fewer_than_min_samples()
    {
        // Only 2 files with models — below MinModelSamples=3
        var items = new[]
        {
            Item("a.jpg", Base, "Canon EOS R5"),
            Item("b.jpg", Base.AddMinutes(5), "Samsung Galaxy S24"),
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.DoesNotContain(result, r => r.Reasons.Any(r2 => r2.Contains("Camera model")));
    }

    [Fact]
    public void Can_flag_both_timestamp_and_camera_outlier_on_same_file()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .Append(Item("suspect.jpg", Base.AddDays(90), "Samsung Galaxy S24"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var suspect = result.FirstOrDefault(r => r.Path.Contains("suspect"));
        Assert.NotNull(suspect);
        Assert.True(suspect!.Reasons.Count >= 2);
    }

    [Fact]
    public void Returns_empty_for_empty_input()
    {
        var result = SessionOutlierDetector.Detect([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Normal_2_hour_gap_within_session_is_not_flagged()
    {
        // Lunch break between shots — should not be an outlier
        var items = new[]
        {
            Item("morning1.jpg", Base.AddHours(9), "Canon EOS R5"),
            Item("morning2.jpg", Base.AddHours(9).AddMinutes(30), "Canon EOS R5"),
            Item("morning3.jpg", Base.AddHours(10), "Canon EOS R5"),
            Item("afternoon1.jpg", Base.AddHours(13), "Canon EOS R5"),
            Item("afternoon2.jpg", Base.AddHours(13).AddMinutes(30), "Canon EOS R5"),
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        Assert.Empty(result);
    }
}
