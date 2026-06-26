using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class ThumbnailMismatchAnalyzerTests
{
    [Fact]
    public void Returns_empty_when_main_dimensions_missing()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(null, null, 160, 120);
        Assert.Empty(results);
    }

    [Fact]
    public void Returns_empty_when_thumb_dimensions_missing()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4032, 3024, null, null);
        Assert.Empty(results);
    }

    [Fact]
    public void Returns_consistent_for_proportional_thumb()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4032, 3024, 160, 120);
        Assert.Single(results, r => r.Id == "thumb-dimensions-consistent");
    }

    [Fact]
    public void Detects_thumbnail_larger_than_main()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(640, 480, 1280, 960);
        var match = Assert.Single(results, r => r.Id == "thumb-larger-than-main");
        Assert.Equal(ReviewPriority.High, match.ReviewPriority);
    }

    [Fact]
    public void Detects_aspect_ratio_mismatch()
    {
        // Main: 4:3, Thumb: 16:9
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 320, 180);
        Assert.Contains(results, r => r.Id == "thumb-aspect-ratio-mismatch");
        Assert.All(results.Where(r => r.Id == "thumb-aspect-ratio-mismatch"),
            r => Assert.Equal(ReviewPriority.Medium, r.ReviewPriority));
    }

    [Fact]
    public void Detects_orientation_mismatch_portrait_vs_landscape()
    {
        // Main: landscape 4:3, Thumb: portrait 3:4 (transposed)
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 120, 160);
        Assert.Contains(results, r => r.Id == "thumb-orientation-mismatch");
    }

    [Fact]
    public void Thumbnail_same_size_as_main_returns_consistent()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(640, 480, 640, 480);
        Assert.Contains(results, r => r.Id == "thumb-dimensions-consistent");
    }

    [Fact]
    public void Larger_thumb_finding_has_high_confidence()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(100, 100, 200, 200);
        var match = Assert.Single(results, r => r.Id == "thumb-larger-than-main");
        Assert.True(match.ObservationConfidence.Value >= 90);
    }

    [Fact]
    public void Aspect_mismatch_includes_dimension_detail_in_observation()
    {
        var results = ThumbnailMismatchAnalyzer.Analyze(4000, 3000, 320, 180);
        var match = results.First(r => r.Id == "thumb-aspect-ratio-mismatch");
        Assert.Contains("320", match.Observation);
        Assert.Contains("180", match.Observation);
    }
}
