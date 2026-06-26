using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class JpegIntegrityAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    // ── helpers ──────────────────────────────────────────────────────────────

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

    // ── too small ────────────────────────────────────────────────────────────

    [Fact]
    public void TooSmall_ReturnsCriticalFinding()
    {
        var path = TempFile(0xFF, 0xD8); // only SOI — under the 4-byte minimum
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("jpeg-too-small", findings[0].Id);
        Assert.Equal(ReviewPriority.Critical, findings[0].ReviewPriority);
    }

    // ── missing / wrong SOI ──────────────────────────────────────────────────

    [Fact]
    public void WrongSoi_ReturnsHighFinding()
    {
        var path = TempFile(0x01, 0x02, 0x03, 0x04);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.Single(findings);
        Assert.Equal("jpeg-missing-soi", findings[0].Id);
        Assert.Equal(ReviewPriority.High, findings[0].ReviewPriority);
    }

    // ── intact structure ─────────────────────────────────────────────────────

    [Fact]
    public void ValidJpeg_ReturnsStructureOk()
    {
        // Minimal valid JPEG: SOI + one byte + EOI
        var path = TempFile(0xFF, 0xD8, 0xAB, 0xFF, 0xD9);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var ok = findings.FirstOrDefault(f => f.Id == "jpeg-structure-ok");
        Assert.NotNull(ok);
        Assert.Equal(ReviewPriority.None, ok.ReviewPriority);
    }

    // ── truncated (EOI absent) ───────────────────────────────────────────────

    [Fact]
    public void TruncatedJpeg_ReturnsMissingEoi()
    {
        // SOI + image data, no EOI at all
        var path = TempFile(0xFF, 0xD8, 0xAB, 0xCD, 0xEF, 0x01);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var f = findings.FirstOrDefault(f => f.Id == "jpeg-missing-eoi");
        Assert.NotNull(f);
        Assert.Equal(ReviewPriority.Medium, f.ReviewPriority);
    }

    // ── appended data ────────────────────────────────────────────────────────

    [Fact]
    public void AppendedData_ReturnsAppendedDataFinding()
    {
        // SOI + EOI + 3 garbage bytes appended after
        var path = TempFile(0xFF, 0xD8, 0xFF, 0xD9, 0x00, 0x01, 0x02);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var f = findings.FirstOrDefault(f => f.Id == "jpeg-appended-data");
        Assert.NotNull(f);
        Assert.Equal(ReviewPriority.Medium, f.ReviewPriority);
        Assert.Contains("3 byte(s)", f.Observation);
    }

    [Fact]
    public void AppendedData_LargeAppend_CountedCorrectly()
    {
        // 10 bytes appended after EOI
        var payload = new byte[] { 0xFF, 0xD8 }
            .Concat(new byte[50])   // image body
            .Concat(new byte[] { 0xFF, 0xD9 })  // EOI
            .Concat(new byte[10])   // appended garbage
            .ToArray();
        var path = TempFile(payload);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var f = findings.FirstOrDefault(f => f.Id == "jpeg-appended-data");
        Assert.NotNull(f);
        Assert.Equal("jpeg-appended-data", f.Id);
        Assert.Contains("10 byte(s)", f.Observation);
    }

    // ── segment walk: truncated segment ─────────────────────────────────────

    [Fact]
    public void TruncatedSegment_ReturnsHighFinding()
    {
        // SOI + APP0 marker claiming 100 bytes of data, but file ends after 4 bytes.
        // segLen = 0x0064 = 100 means 98 bytes of data follow the length field.
        var path = TempFile(
            0xFF, 0xD8,       // SOI
            0xFF, 0xE0,       // APP0 marker
            0x00, 0x64,       // length = 100 (only 4 bytes actually present)
            0x01, 0x02        // truncated — only 2 of 98 expected bytes
        );
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.Contains(findings, f => f.Id == "jpeg-truncated-segment");
        Assert.All(
            findings.Where(f => f.Id == "jpeg-truncated-segment"),
            f => Assert.Equal(ReviewPriority.High, f.ReviewPriority));
    }

    [Fact]
    public void MalformedSegmentLength_ReturnsHighFinding()
    {
        // APP0 with length = 1 — below the 2-byte minimum.
        var path = TempFile(
            0xFF, 0xD8,       // SOI
            0xFF, 0xE0,       // APP0 marker
            0x00, 0x01,       // length = 1 (invalid — minimum is 2)
            0xFF, 0xD9        // EOI (won't be reached through the walk)
        );
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.Contains(findings, f => f.Id == "jpeg-malformed-segment-length");
        Assert.Equal(ReviewPriority.High,
            findings.First(f => f.Id == "jpeg-malformed-segment-length").ReviewPriority);
    }

    // ── segment walk: SOS without SOF ────────────────────────────────────────

    [Fact]
    public void SosWithoutSof_ReturnsMediumFinding()
    {
        // SOI + SOS immediately (no SOF anywhere before it).
        // Walker sees SOS, sets sosSeen=true; sofSeen remains false.
        var path = TempFile(
            0xFF, 0xD8,                               // SOI
            0xFF, 0xDA,                               // SOS marker
            0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,  // minimal SOS header
            0xFF, 0xD9                                // EOI in the tail
        );
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.Contains(findings, f => f.Id == "jpeg-no-sof-before-sos");
        Assert.Equal(ReviewPriority.Medium,
            findings.First(f => f.Id == "jpeg-no-sof-before-sos").ReviewPriority);
    }
    // ── APP segment inventory ─────────────────────────────────────────────────

    // SOI + APP1(EXIF, 16 bytes data) + EOI
    // segLen = 18 (0x00 0x12) → dataLen = 16
    private static byte[] Jpeg(params byte[] segments) =>
        [0xFF, 0xD8, .. segments, 0xFF, 0xD9];

    private static byte[] App1Exif() =>
    [
        0xFF, 0xE1, 0x00, 0x12,                                              // APP1, length=18
        0x45, 0x78, 0x69, 0x66, 0x00, 0x00,                                 // "Exif\0\0"
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00          // 10 padding
    ];

    // "http://ns.adobe.com" prefix is enough to identify as XMP (19 bytes → segLen=21)
    private static byte[] App1Xmp() =>
    [
        0xFF, 0xE1, 0x00, 0x15,                                              // APP1, length=21
        0x68, 0x74, 0x74, 0x70, 0x3A, 0x2F, 0x2F,                          // "http://"
        0x6E, 0x73, 0x2E, 0x61, 0x64, 0x6F, 0x62, 0x65, 0x2E, 0x63, 0x6F, 0x6D  // "ns.adobe.com"
    ];

    [Fact]
    public void NormalExif_ReturnsMetadataLayersFinding()
    {
        var path = TempFile(Jpeg(App1Exif()));
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var layer = findings.FirstOrDefault(f => f.Id == "jpeg-metadata-layers");
        Assert.NotNull(layer);
        Assert.Contains("EXIF", layer.Observation);
        Assert.DoesNotContain(findings, f => f.Id == "jpeg-no-app-metadata");
    }

    [Fact]
    public void DuplicateExif_ReturnsDuplicateExifFinding()
    {
        var path = TempFile(Jpeg([.. App1Exif(), .. App1Exif()]));
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var dup = findings.FirstOrDefault(f => f.Id == "jpeg-duplicate-exif");
        Assert.NotNull(dup);
        Assert.Equal(ReviewPriority.Medium, dup.ReviewPriority);
        Assert.Contains("2 times", dup.Observation);
    }

    [Fact]
    public void ExifPlusXmp_DoesNotFlagDuplicateExif()
    {
        // EXIF + XMP in two APP1 segments is completely normal.
        var path = TempFile(Jpeg([.. App1Exif(), .. App1Xmp()]));
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        Assert.DoesNotContain(findings, f => f.Id == "jpeg-duplicate-exif");
        var layer = findings.FirstOrDefault(f => f.Id == "jpeg-metadata-layers");
        Assert.NotNull(layer);
        Assert.Contains("EXIF", layer.Observation);
        Assert.Contains("XMP",  layer.Observation);
    }

    [Fact]
    public void NoAppMetadata_ReturnsNoAppMetadataFinding()
    {
        // SOI + EOI only — no APP segments at all.
        var path = TempFile(0xFF, 0xD8, 0xFF, 0xD9);
        var findings = JpegIntegrityAnalyzer.Analyze(path);
        var noApp = findings.FirstOrDefault(f => f.Id == "jpeg-no-app-metadata");
        Assert.NotNull(noApp);
        Assert.Equal(ReviewPriority.Low, noApp.ReviewPriority);
        Assert.DoesNotContain(findings, f => f.Id == "jpeg-metadata-layers");
    }
}
