using Xunit;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class PngIntegrityAnalyzerTests : IDisposable
{
    private static readonly byte[] s_sig =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] s_iend =
        [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    private readonly List<string> _tempFiles = [];

    // ── helpers ──────────────────────────────────────────────────────────────

    private string TempFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // Sig (8) + padding (N) + IEND (12)
    private byte[] ValidPng(int paddingBytes = 10) =>
        [.. s_sig, .. new byte[paddingBytes], .. s_iend];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // ── too small ────────────────────────────────────────────────────────────

    [Fact]
    public void TooSmall_ReturnsCriticalFinding()
    {
        var path = TempFile(new byte[10]); // under 20-byte minimum
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-too-small", findings[0].Id);
        Assert.Equal(ReviewPriority.Critical, findings[0].ReviewPriority);
    }

    // ── invalid signature ────────────────────────────────────────────────────

    [Fact]
    public void WrongSignature_ReturnsHighFinding()
    {
        var data = new byte[30]; // all zeros — wrong signature
        var path = TempFile(data);
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-invalid-signature", findings[0].Id);
        Assert.Equal(ReviewPriority.High, findings[0].ReviewPriority);
    }

    // ── intact structure ─────────────────────────────────────────────────────

    [Fact]
    public void ValidPng_ReturnsStructureOk()
    {
        var path = TempFile(ValidPng());
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-structure-ok", findings[0].Id);
        Assert.Equal(ReviewPriority.None, findings[0].ReviewPriority);
    }

    // ── missing IEND ─────────────────────────────────────────────────────────

    [Fact]
    public void MissingIend_ReturnsMissingIendFinding()
    {
        // Valid signature but no IEND — just padding bytes
        var data = s_sig.Concat(new byte[30]).ToArray();
        var path = TempFile(data);
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-missing-iend", findings[0].Id);
        Assert.Equal(ReviewPriority.Medium, findings[0].ReviewPriority);
    }

    // ── appended data ────────────────────────────────────────────────────────

    [Fact]
    public void AppendedData_ReturnsAppendedDataFinding()
    {
        // Valid PNG with 5 garbage bytes after IEND
        var data = ValidPng().Concat(new byte[5]).ToArray();
        var path = TempFile(data);
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-appended-data", findings[0].Id);
        Assert.Equal(ReviewPriority.Medium, findings[0].ReviewPriority);
        Assert.Contains("5 byte(s)", findings[0].Observation);
    }

    [Fact]
    public void AppendedData_LargeAppend_CountedCorrectly()
    {
        var data = ValidPng(paddingBytes: 20).Concat(new byte[15]).ToArray();
        var path = TempFile(data);
        var findings = PngIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("png-appended-data", findings[0].Id);
        Assert.Contains("15 byte(s)", findings[0].Observation);
    }
}
