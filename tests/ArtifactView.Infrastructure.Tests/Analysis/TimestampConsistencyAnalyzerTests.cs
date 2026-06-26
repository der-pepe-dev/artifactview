using System.IO;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

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

    [Test]
    public async Task NullCaptureDate_ReturnsEmpty()
    {
        var path = TempFile();
        await Assert.That(TimestampConsistencyAnalyzer.Analyze(path, null)).IsEmpty();
    }

    // ── filesystem vs DateTimeOriginal ────────────────────────────────────────

    [Test]
    public async Task FilesystemWithin24Hours_ReturnsConsistent()
    {
        var path = TempFile();
        // Use the file's own write time so the difference is effectively zero.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime();
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        await Assert.That(findings).Contains(f => f.Id == "ts-fs-consistent");
    }

    [Test]
    public async Task FilesystemMuchLater_ReturnsFsLater()
    {
        var path = TempFile();
        // Pretend the capture happened 60 days before the file was written.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime().AddDays(-60);
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        await Assert.That(findings).Contains(f => f.Id == "ts-fs-later");
        await Assert.That(findings.First(f => f.Id == "ts-fs-later").ReviewPriority).IsEqualTo(ReviewPriority.Low);
    }

    [Test]
    public async Task FilesystemPrecedesCapture_ReturnsFsPrecedes()
    {
        var path = TempFile();
        // Pretend the capture is 60 days AFTER the file was written.
        var captureDate = File.GetLastWriteTimeUtc(path).ToLocalTime().AddDays(60);
        var findings = TimestampConsistencyAnalyzer.Analyze(path, captureDate);
        await Assert.That(findings).Contains(f => f.Id == "ts-fs-precedes-exif");
        await Assert.That(findings.First(f => f.Id == "ts-fs-precedes-exif").ReviewPriority).IsEqualTo(ReviewPriority.Medium);
    }

    // ── IFD0 DateTime vs DateTimeOriginal ─────────────────────────────────────

    [Test]
    public async Task Ifd0Matches_ReturnsConsistent()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeModified: captureDate);
        await Assert.That(findings).Contains(f => f.Id == "ts-ifd0-consistent");
    }

    [Test]
    public async Task Ifd0DiffersByDays_ReturnsIfd0Differs()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var modified    = captureDate.AddDays(5);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeModified: modified);
        await Assert.That(findings).Contains(f => f.Id == "ts-ifd0-differs");
        await Assert.That(findings.First(f => f.Id == "ts-ifd0-differs").ReviewPriority).IsEqualTo(ReviewPriority.Medium);
    }

    // ── DateTimeDigitized vs DateTimeOriginal ─────────────────────────────────

    [Test]
    public async Task DigitizedMatchesCapture_NoDigitizedFinding()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeDigitized: captureDate);
        await Assert.That(findings).DoesNotContain(f => f.Id == "ts-digitized-differs");
    }

    [Test]
    public async Task DigitizedDiffersByHours_ReturnsDigitizedDiffers()
    {
        var path = TempFile();
        var captureDate = new DateTime(2024, 6, 15, 10, 0, 0);
        var digitized   = captureDate.AddHours(3);
        var findings = TimestampConsistencyAnalyzer.Analyze(
            path, captureDate, dateTimeDigitized: digitized);
        await Assert.That(findings).Contains(f => f.Id == "ts-digitized-differs");
        await Assert.That(findings.First(f => f.Id == "ts-digitized-differs").ReviewPriority).IsEqualTo(ReviewPriority.Low);
    }
}