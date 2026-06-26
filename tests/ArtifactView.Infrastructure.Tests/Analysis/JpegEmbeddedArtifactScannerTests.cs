using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class JpegEmbeddedArtifactScannerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string TempFile(params byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // Builds a minimal valid JPEG: SOI + SOS (empty header) + EOI, optionally
    // followed by trailing data.
    private static byte[] MinimalJpeg(byte[]? trailing = null)
    {
        // SOI  FF D8
        // SOS  FF DA 00 02  (length=2, meaning 0 extra header bytes)
        // EOI  FF D9
        var body = new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9 };
        if (trailing is null) return body;
        var combined = new byte[body.Length + trailing.Length];
        body.CopyTo(combined, 0);
        trailing.CopyTo(combined, body.Length);
        return combined;
    }

    [Fact]
    public void NonJpeg_ReturnsEmpty()
    {
        var path = TempFile(0x89, 0x50, 0x4E, 0x47); // PNG header
        Assert.Empty(JpegEmbeddedArtifactScanner.Scan(path));
    }

    [Fact]
    public void MinimalJpeg_NoTrailing_ReturnsEmpty()
    {
        var path = TempFile(MinimalJpeg());
        Assert.Empty(JpegEmbeddedArtifactScanner.Scan(path));
    }

    [Fact]
    public void TrailingJpeg_DetectedAsSecondaryImage()
    {
        // Main JPEG followed by a secondary JPEG (SOI + some bytes)
        var trailing = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x00, 0x00 };
        var path = TempFile(MinimalJpeg(trailing));

        var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
        Assert.Single(artifacts);

        var a = artifacts[0];
        Assert.Equal(EmbeddedArtifactType.SecondaryImage, a.Type);
        Assert.NotNull(a.Offset);
        Assert.NotNull(a.Length);
        Assert.Equal(trailing.Length, (int)a.Length!.Value);
    }

    [Fact]
    public void TrailingMp4_DetectedAsMotionPhoto()
    {
        // ftyp box at trailing data start
        var trailing = new byte[]
        {
            0x00, 0x00, 0x00, 0x14, // box size
            0x66, 0x74, 0x79, 0x70, // "ftyp"
            0x6D, 0x70, 0x34, 0x32, // "mp42"
            0x00, 0x00, 0x00, 0x00,
            0x6D, 0x70, 0x34, 0x31  // "mp41"
        };
        var path = TempFile(MinimalJpeg(trailing));

        var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
        Assert.Single(artifacts);
        Assert.Equal(EmbeddedArtifactType.MotionPhotoVideo, artifacts[0].Type);
        Assert.Equal("video/mp4", artifacts[0].MimeType);
    }

    [Fact]
    public void UnknownTrailing_Detected()
    {
        var trailing = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var path = TempFile(MinimalJpeg(trailing));

        var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
        Assert.Single(artifacts);
        Assert.Equal(EmbeddedArtifactType.Unknown, artifacts[0].Type);
    }

    [Fact]
    public void TrailingArtifact_IsExtractable()
    {
        var trailing = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xAA, 0xBB };
        var path = TempFile(MinimalJpeg(trailing));

        var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
        Assert.Single(artifacts);

        var payload = JpegEmbeddedArtifactScanner.ExtractPayload(path, artifacts[0]);
        Assert.NotNull(payload);
        Assert.Equal(trailing, payload);
    }

    [Fact]
    public void EoiInsideAppSegment_NotMistakenForMainEoi()
    {
        // SOI + APP1 segment containing FF D9 bytes (simulating EXIF thumbnail EOI)
        // + SOS + entropy + real EOI.  Should NOT produce trailing data.
        var file = new byte[]
        {
            0xFF, 0xD8,             // SOI
            0xFF, 0xE1,             // APP1 marker
            0x00, 0x06,             // length = 6 (includes length bytes)
            0xFF, 0xD9, 0xAA, 0xBB, // payload containing FF D9 (fake EOI)
            0xFF, 0xDA,             // SOS marker
            0x00, 0x02,             // SOS length = 2
            0xFF, 0xD9              // real EOI
        };
        var path = TempFile(file);
        Assert.Empty(JpegEmbeddedArtifactScanner.Scan(path));
    }
}
