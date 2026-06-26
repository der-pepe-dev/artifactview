using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class ThumbnailMismatchAnalyzerTests
{
    [Test]
    public async Task Returns_empty_when_main_dimensions_missing()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(null, null, 160, 120);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Returns_empty_when_thumb_dimensions_missing()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4032, 3024, null, null);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Returns_consistent_for_proportional_thumb()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4032, 3024, 160, 120);
        await Assert.That(results.Where(r => r.Id == "thumb-dimensions-consistent")).HasSingleItem();
    }

    [Test]
    public async Task Detects_thumbnail_larger_than_main()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(640, 480, 1280, 960);
        var match = await Assert.That(results.Where(r => r.Id == "thumb-larger-than-main")).HasSingleItem();
        await Assert.That(match.ReviewPriority).IsEqualTo(ReviewPriority.High);
    }

    [Test]
    public async Task Detects_aspect_ratio_mismatch()
    {
        // Main: 4:3, Thumb: 16:9
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 320, 180);
        await Assert.That(results).Contains(r => r.Id == "thumb-aspect-ratio-mismatch");
        foreach (var r in results.Where(r => r.Id == "thumb-aspect-ratio-mismatch"))
            await Assert.That(r.ReviewPriority).IsEqualTo(ReviewPriority.Medium);
    }

    [Test]
    public async Task Detects_orientation_mismatch_portrait_vs_landscape()
    {
        // Main: landscape 4:3, Thumb: portrait 3:4 (transposed)
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 120, 160);
        await Assert.That(results).Contains(r => r.Id == "thumb-orientation-mismatch");
    }

    [Test]
    public async Task Thumbnail_same_size_as_main_returns_consistent()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(640, 480, 640, 480);
        await Assert.That(results).Contains(r => r.Id == "thumb-dimensions-consistent");
    }

    [Test]
    public async Task Larger_thumb_finding_has_high_confidence()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(100, 100, 200, 200);
        var match = await Assert.That(results).HasSingleItem();
        await Assert.That(match.ObservationConfidence.Value >= 90).IsTrue();
    }

    [Test]
    public async Task Aspect_mismatch_includes_dimension_detail_in_observation()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 320, 180);
        var match = results.First(r => r.Id == "thumb-aspect-ratio-mismatch");
        await Assert.That(match.Observation).Contains("320");
        await Assert.That(match.Observation).Contains("180");
    }
}