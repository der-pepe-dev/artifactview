using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class SequenceGapDetectorTests
{
    private static (string Path, string DisplayName) F(string name)
        => ($"/folder/{name}", name);

    [Test]
    public async Task Returns_empty_for_no_files()
        => await Assert.That(SequenceGapDetector.Detect([])).IsEmpty();

    [Test]
    public async Task Returns_empty_for_single_file()
        => await Assert.That(SequenceGapDetector.Detect([F("IMG_0001.jpg")])).IsEmpty();

    [Test]
    public async Task Returns_empty_for_contiguous_sequence()
    {
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0002.jpg"), F("IMG_0003.jpg") };
        await Assert.That(SequenceGapDetector.Detect(files)).IsEmpty();
    }

    [Test]
    public async Task Detects_gap_of_two_missing_frames()
    {
        // 0001, 0004 → missing 0002, 0003 → gap = 2
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0004.jpg") };
        var gaps  = SequenceGapDetector.Detect(files);
        var gap   = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.LastBefore).IsEqualTo(1);
        await Assert.That(gap.FirstAfter).IsEqualTo(4);
        await Assert.That(gap.MissingCount).IsEqualTo(2);
    }

    [Test]
    public async Task Single_missing_frame_not_reported()
    {
        // Gap of 1 is below MinGapSize threshold.
        var files = new[] { F("IMG_0001.jpg"), F("IMG_0003.jpg") };
        await Assert.That(SequenceGapDetector.Detect(files)).IsEmpty();
    }

    [Test]
    public async Task Reports_multiple_gaps_in_sequence()
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
        await Assert.That(gaps.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Groups_by_prefix_independently()
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
        var gap  = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.Prefix).IsEqualTo("IMG_");
    }

    [Test]
    public async Task Files_without_trailing_digits_are_skipped()
    {
        var files = new[]
        {
            F("vacation.jpg"),     // no trailing digits
            F("sunset.png"),       // no trailing digits
            F("IMG_0001.jpg"),
            F("IMG_0010.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.Prefix).IsEqualTo("IMG_");
    }

    [Test]
    public async Task PathBefore_and_PathAfter_are_correct()
    {
        var files = new[]
        {
            ("/folder/IMG_0001.jpg", "IMG_0001.jpg"),
            ("/folder/IMG_0005.jpg", "IMG_0005.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.PathBefore).IsEqualTo("/folder/IMG_0001.jpg");
        await Assert.That(gap.PathAfter).IsEqualTo("/folder/IMG_0005.jpg");
    }

    [Test]
    public async Task Handles_unordered_input()
    {
        // Files given out of order — detector must sort internally.
        var files = new[]
        {
            F("IMG_0010.jpg"),
            F("IMG_0001.jpg"),
            F("IMG_0002.jpg"),
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.LastBefore).IsEqualTo(2);
        await Assert.That(gap.FirstAfter).IsEqualTo(10);
    }

    [Test]
    public async Task GoPro_prefix_detected_correctly()
    {
        var files = new[]
        {
            F("GOPR0100.MP4"),
            F("GOPR0110.MP4"),  // gap of 9
        };
        var gaps = SequenceGapDetector.Detect(files);
        var gap  = await Assert.That(gaps).HasSingleItem();
        await Assert.That(gap.Prefix).IsEqualTo("GOPR");
        await Assert.That(gap.MissingCount).IsEqualTo(9);
    }
}