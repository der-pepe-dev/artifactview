using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SequenceGapDetectorTests
{
    private static (string Path, string DisplayName) F(string name)
        => ($"/folder/{name}", name);

    [Fact]
    public void Returns_empty_for_no_files()
        => Assert.Empty(SequenceGapDetector.Detect([]));

    [Fact]
    public void Returns_empty_for_single_file()
        => Assert.Empty(SequenceGapDetector.Detect([F("IMG_0001.jpg")]));

    [Fact]
    public void Returns_empty_for_contiguous_sequence()
    {
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0002.jpg"), F("IMG_0003.jpg") };
        Assert.Empty(SequenceGapDetector.Detect(files));
    }

    [Fact]
    public void Detects_gap_of_two_missing_frames()
    {
        // 0001, 0004 → missing 0002, 0003 → gap = 2
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0004.jpg") };
        var gaps  = SequenceGapDetector.Detect(files);
        var gap   = Assert.Single(gaps);
        Assert.Equal(1, gap.LastBefore);
        Assert.Equal(4, gap.FirstAfter);
        Assert.Equal(2, gap.MissingCount);
    }

    [Fact]
    public void Single_missing_frame_not_reported()
    {
        // Gap of 1 is below MinGapSize threshold.
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0003.jpg") };
        Assert.Empty(SequenceGapDetector.Detect(files));
    }

    [Fact]
    public void Reports_multiple_gaps_in_sequence()
    {
        var files = new[]
        {
            F("IMG_0001.jpg"),
            F("IMG_0002.jpg"),
            F("IMG_0010.jpg"),  // gap of 7
            F("IMG_0011.jpg"),
            F("IMG_0020.jpg"),  // gap of 8
        };
        var gaps = SequenceGapDetector.Detect(files);
        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Groups_by_prefix_independently()
    {
        // IMG_ and DSC_ are separate camera sequences.
        var files = new[]
        {
            F("IMG_0001.jpg"),
            F("IMG_0010.jpg"),  // gap of 8 in IMG_
            F("DSC_0001.jpg"),
            F("DSC_0002.jpg"),  // contiguous — no gap
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = Assert.Single(gaps);
        Assert.Equal("IMG_", gap.Prefix);
    }

    [Fact]
    public void Files_without_trailing_digits_are_skipped()
    {
        var files = new[]
        {
            F("vacation.jpg"),     // no trailing digits
            F("sunset.png"),       // no trailing digits
            F("IMG_0001.jpg"),
            F("IMG_0010.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = Assert.Single(gaps);
        Assert.Equal("IMG_", gap.Prefix);
    }

    [Fact]
    public void PathBefore_and_PathAfter_are_correct()
    {
        var files = new[]
        {
            ("/folder/IMG_0001.jpg", "IMG_0001.jpg"),
            ("/folder/IMG_0005.jpg", "IMG_0005.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = Assert.Single(gaps);
        Assert.Equal("/folder/IMG_0001.jpg", gap.PathBefore);
        Assert.Equal("/folder/IMG_0005.jpg", gap.PathAfter);
    }

    [Fact]
    public void Handles_unordered_input()
    {
        // Files given out of order — detector must sort internally.
        var files = new[]
        {
            F("IMG_0010.jpg"),
            F("IMG_0001.jpg"),
            F("IMG_0002.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = Assert.Single(gaps);
        Assert.Equal(2, gap.LastBefore);
        Assert.Equal(10, gap.FirstAfter);
    }

    [Fact]
    public void GoPro_prefix_detected_correctly()
    {
        var files = new[]
        {
            F("GOPR0100.MP4"),
            F("GOPR0110.MP4"),  // gap of 9
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = Assert.Single(gaps);
        Assert.Equal("GOPR", gap.Prefix);
        Assert.Equal(9, gap.MissingCount);
    }
}
