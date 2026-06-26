using Xunit;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class TimestampConsistencyAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, [0x00]);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // ── null / missing ───────────────────────────────────────────────────────

    [Fact]
    public void NullCaptureDate_ReturnsEmpty()
    {
        var path = TempFile();
        Assert.Empty(TimestampConsistencyAnalyzer.Analyze(path, null));
    }

    // ── filesystem vs DateTimeOriginal ────────────────────────────────────────

    [Fact]
    public void FilesystemWithin24Hours_ReturnsConsistent()
    {
        var path = TempFile();
        // Use the file's own write time so the difference is effectively zero.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime();
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        Assert.Contains(findings, f => f.Id == "ts-fs-consistent");
    }

    [Fact]
    public void FilesystemMuchLater_ReturnsFsLater()
    {
        var path = TempFile();
        // Pretend the capture happened 60 days before the file was written.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime().AddDays(-60);
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        Assert.Contains(findings, f => f.Id == "ts-fs-later");
        Assert.Equal(ReviewPriority.Low,
            findings.First(f => f.Id == "ts-fs-later").ReviewPriority);
    }

    [Fact]
    public void FilesystemPrecedesCapture_ReturnsFsPrecedes()
    {
        var path = TempFile();
        // Pretend the capture is 60 days AFTER the file was written.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime().AddDays(60);
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        Assert.Contains(findings, f => f.Id == "ts-fs-precedes-exif");
        Assert.Equal(ReviewPriority.Medium,
            findings.First(f => f.Id == "ts-fs-precedes-exif").ReviewPriority);
    }

    // ── IFD0 DateTime vs DateTimeOriginal ─────────────────────────────────────

    [Fact]
    public void Ifd0Matches_ReturnsConsistent()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeModified: captureDate);
        Assert.Contains(findings, f => f.Id == "ts-ifd0-consistent");
    }

    [Fact]
    public void Ifd0DiffersByDays_ReturnsIfd0Differs()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var modified    = captureDate.AddDays(5);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeModified: modified);
        Assert.Contains(findings, f => f.Id == "ts-ifd0-differs");
        Assert.Equal(ReviewPriority.Medium,
            findings.First(f => f.Id == "ts-ifd0-differs").ReviewPriority);
    }

    // ── DateTimeDigitized vs DateTimeOriginal ─────────────────────────────────

    [Fact]
    public void DigitizedMatchesCapture_NoDigitizedFinding()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeDigitized: captureDate);
        Assert.DoesNotContain(findings, f => f.Id == "ts-digitized-differs");
    }

    [Fact]
    public void DigitizedDiffersByHours_ReturnsDigitizedDiffers()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var digitized   = captureDate.AddHours(3);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeDigitized: digitized);
        Assert.Contains(findings, f => f.Id == "ts-digitized-differs");
        Assert.Equal(ReviewPriority.Low,
            findings.First(f => f.Id == "ts-digitized-differs").ReviewPriority);
    }
}
