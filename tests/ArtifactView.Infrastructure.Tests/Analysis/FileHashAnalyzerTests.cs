using Xunit;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class FileHashAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempFile(params byte[] content)
    {
        var path = Path.GetTempFileName();
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
    public void ValidFile_ReturnsSha256Finding()
    {
        var path = TempFile(0x01, 0x02, 0x03);
        var finding = FileHashAnalyzer.Analyze(path);
        Assert.Equal("file-hash-sha256", finding.Id);
        Assert.Equal(ReviewPriority.None, finding.ReviewPriority);
        Assert.Equal("Provenance", finding.Category);
    }

    [Fact]
    public void FullHexStoredInSupportingFactors()
    {
        var path = TempFile(0xAA, 0xBB, 0xCC);
        var finding = FileHashAnalyzer.Analyze(path);
        Assert.Single(finding.SupportingFactors);
        // SHA-256 = 64 hex characters
        Assert.Equal(64, finding.SupportingFactors[0].Length);
        // All hex characters
        Assert.Matches("^[0-9A-F]{64}$", finding.SupportingFactors[0]);
    }

    [Fact]
    public void HashIsDeterministic()
    {
        var path = TempFile(0xDE, 0xAD, 0xBE, 0xEF);
        var h1 = FileHashAnalyzer.Analyze(path).SupportingFactors[0];
        var h2 = FileHashAnalyzer.Analyze(path).SupportingFactors[0];
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DifferentContent_DifferentHash()
    {
        var p1 = TempFile(0x01);
        var p2 = TempFile(0x02);
        var h1 = FileHashAnalyzer.Analyze(p1).SupportingFactors[0];
        var h2 = FileHashAnalyzer.Analyze(p2).SupportingFactors[0];
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void NonExistentFile_ReturnsErrorFinding()
    {
        var finding = FileHashAnalyzer.Analyze(@"C:\nonexistent_path_12345.tmp");
        Assert.Equal("file-hash-error", finding.Id);
        Assert.Equal(ReviewPriority.Low, finding.ReviewPriority);
    }
}
