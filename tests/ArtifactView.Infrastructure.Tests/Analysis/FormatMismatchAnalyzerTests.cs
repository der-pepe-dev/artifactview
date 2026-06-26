using System.IO;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class FormatMismatchAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempFile(string extension, params byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    [Test]
    public async Task Jpeg_WithJpgExtension_ReportsMatch()
    {
        var path = TempFile(".jpg", 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        await Assert.That(finding.Id).IsEqualTo("format-match");
        await Assert.That(finding.ReviewPriority).IsEqualTo(ReviewPriority.None);
    }

    [Test]
    public async Task Png_WithJpgExtension_ReportsMismatch()
    {
        // PNG magic bytes but .jpg extension
        var path = TempFile(".jpg", 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        await Assert.That(finding.Id).IsEqualTo("format-mismatch");
        await Assert.That(finding.ReviewPriority).IsEqualTo(ReviewPriority.High);
    }

    [Test]
    public async Task Jpeg_WithPngExtension_ReportsMismatch()
    {
        var path = TempFile(".png", 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        await Assert.That(finding.Id).IsEqualTo("format-mismatch");
    }

    [Test]
    public void UnknownExtension_ReturnsNull()
    {
        var path = TempFile(".xyz", 0xFF, 0xD8, 0xFF, 0xE0);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.Null(finding);
    }

    [Test]
    public void UnknownContent_ReturnsNull()
    {
        var path = TempFile(".jpg", 0x00, 0x01, 0x02);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.Null(finding);
    }
}