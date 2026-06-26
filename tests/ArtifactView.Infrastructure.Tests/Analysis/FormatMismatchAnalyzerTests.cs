using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using Xunit;

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

    [Fact]
    public void Jpeg_WithJpgExtension_ReportsMatch()
    {
        var path = TempFile(".jpg", 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        Assert.Equal("format-match", finding.Id);
        Assert.Equal(ReviewPriority.None, finding.ReviewPriority);
    }

    [Fact]
    public void Png_WithJpgExtension_ReportsMismatch()
    {
        // PNG magic bytes but .jpg extension
        var path = TempFile(".jpg", 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        Assert.Equal("format-mismatch", finding.Id);
        Assert.Equal(ReviewPriority.High, finding.ReviewPriority);
    }

    [Fact]
    public void Jpeg_WithPngExtension_ReportsMismatch()
    {
        var path = TempFile(".png", 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.NotNull(finding);
        Assert.Equal("format-mismatch", finding.Id);
    }

    [Fact]
    public void UnknownExtension_ReturnsNull()
    {
        var path = TempFile(".xyz", 0xFF, 0xD8, 0xFF, 0xE0);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.Null(finding);
    }

    [Fact]
    public void UnknownContent_ReturnsNull()
    {
        var path = TempFile(".jpg", 0x00, 0x01, 0x02);
        var finding = FormatMismatchAnalyzer.Analyze(path);
        Assert.Null(finding);
    }
}
