using System.IO;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

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

    [Test]
    public async Task ValidFile_ReturnsSha256Finding()
    {
        var path = TempFile(0x01, 0x02, 0x03);
        var finding = FileHashAnalyzer.Analyze(path);
        await Assert.That(finding.Id).IsEqualTo("file-hash-sha256");
        await Assert.That(finding.ReviewPriority).IsEqualTo(ReviewPriority.None);
        await Assert.That(finding.Category).IsEqualTo("Provenance");
    }

    [Test]
    public async Task FullHexStoredInSupportingFactors()
    {
        var path = TempFile(0xAA, 0xBB, 0xCC);
        var finding = FileHashAnalyzer.Analyze(path);
        await Assert.That(finding.SupportingFactors).HasSingleItem();
        // SHA-256 = 64 hex characters
        await Assert.That(finding.SupportingFactors[0].Length).IsEqualTo(64);
        // All hex characters
        await Assert.That(finding.SupportingFactors[0]).Matches("^[0-9A-F]{64}$");
    }

    [Test]
    public async Task HashIsDeterministic()
    {
        var path = TempFile(0xDE, 0xAD, 0xBE, 0xEF);
        var h1 = FileHashAnalyzer.Analyze(path).SupportingFactors[0];
        var h2 = FileHashAnalyzer.Analyze(path).SupportingFactors[0];
        await Assert.That(h2).IsEqualTo(h1);
    }

    [Test]
    public async Task DifferentContent_DifferentHash()
    {
        var p1 = TempFile(0x01);
        var p2 = TempFile(0x02);
        var h1 = FileHashAnalyzer.Analyze(p1).SupportingFactors[0];
        var h2 = FileHashAnalyzer.Analyze(p2).SupportingFactors[0];
        await Assert.That(h2).IsNotEqualTo(h1);
    }

    [Test]
    public async Task NonExistentFile_ReturnsErrorFinding()
    {
        var finding = FileHashAnalyzer.Analyze(@"C:\nonexistent_path_12345.tmp");
        await Assert.That(finding.Id).IsEqualTo("file-hash-error");
        await Assert.That(finding.ReviewPriority).IsEqualTo(ReviewPriority.Low);
    }
}