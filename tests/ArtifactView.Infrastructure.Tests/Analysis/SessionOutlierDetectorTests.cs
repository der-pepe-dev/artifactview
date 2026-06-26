using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SessionOutlierDetectorTests
{
    private static readonly DateTime Base = new(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    private static (string Path, DateTime? CaptureTime, string? CameraModel) Item(
        string name, DateTime? time = null, string? model = null) =>
        ($"/photos/{name}", time, model);

    [Test]
    public async Task Returns_empty_for_single_item()
    {
        var result = SessionOutlierDetector.Detect([Item("a.jpg", Base, "Canon EOS R5")]);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Returns_empty_when_all_timestamps_similar()
    {
        var items = Enumerable.Range(0, 6)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Detects_timestamp_outlier_far_in_future()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("outlier.jpg", Base.AddDays(30)))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Path).IsEqualTo("/photos/outlier.jpg");
        await Assert.That(result[0].Reasons).IsNotEmpty();
        await Assert.That(result[0].Reasons[0]).Contains("outside the session");
    }

    [Test]
    public async Task Detects_timestamp_outlier_far_in_past()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("old.jpg", Base.AddDays(-60)))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var paths = result.Select(r => r.Path).ToList();
        await Assert.That(paths).Contains("/photos/old.jpg");
    }

    [Test]
    public async Task Does_not_flag_items_when_fewer_than_min_samples()
    {
        // Only 3 items with timestamps — below MinTimestampSamples=4
        var items = new[]
        {
            Item("a.jpg", Base),
            Item("b.jpg", Base.AddMinutes(5)),
            Item("c.jpg", Base.AddDays(60))   // would be outlier with enough samples
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Ignores_items_without_timestamps()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5)))
            .Append(Item("no_date.jpg", null))   // no timestamp
            .ToList();

        var result = SessionOutlierDetector.Detect(items);
        await Assert.That(result).DoesNotContain(r => r.Path.Contains("no_date"));
    }

    [Test]
    public async Task Detects_camera_model_outlier()
    {
        var items = Enumerable.Range(0, 7)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .Append(Item("other.jpg", Base.AddMinutes(35), "Samsung Galaxy S24"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var cameraOutlier = result.FirstOrDefault(r => r.Path.Contains("other"));
        Assert.NotNull(cameraOutlier);
        await Assert.That(cameraOutlier!.Reasons[0]).Contains("Camera model");
    }

    [Test]
    public async Task Does_not_flag_camera_outlier_when_no_dominant_majority()
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
        await Assert.That(result).DoesNotContain(r => r.Reasons.Any(r2 => r2.Contains("Camera model")));
    }

    [Test]
    public async Task Does_not_flag_camera_outlier_with_fewer_than_min_samples()
    {
        // Only 2 files with models — below MinModelSamples=3
        var items = new[]
        {
            Item("a.jpg", Base, "Canon EOS R5"),
            Item("b.jpg", Base.AddMinutes(5), "Samsung Galaxy S24"),
        }.ToList();

        var result = SessionOutlierDetector.Detect(items);
        await Assert.That(result).DoesNotContain(r => r.Reasons.Any(r2 => r2.Contains("Camera model")));
    }

    [Test]
    public async Task Can_flag_both_timestamp_and_camera_outlier_on_same_file()
    {
        var items = Enumerable.Range(0, 5)
            .Select(i => Item($"p{i}.jpg", Base.AddMinutes(i * 5), "Canon EOS R5"))
            .Append(Item("suspect.jpg", Base.AddDays(90), "Samsung Galaxy S24"))
            .ToList();

        var result = SessionOutlierDetector.Detect(items);

        var suspect = result.FirstOrDefault(r => r.Path.Contains("suspect"));
        Assert.NotNull(suspect);
        await Assert.That(suspect!.Reasons.Count >= 2).IsTrue();
    }

    [Test]
    public async Task Returns_empty_for_empty_input()
    {
        var result = SessionOutlierDetector.Detect([]);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Normal_2_hour_gap_within_session_is_not_flagged()
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
        await Assert.That(result).IsEmpty();
    }
}