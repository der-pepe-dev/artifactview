using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class MagicByteFormatDetectorPluginTests
{
    private readonly MagicByteFormatDetectorPlugin _plugin = new();

    private static MemoryStream S(params byte[] bytes) => new(bytes);

    [Test]
    public async Task Detects_jpeg()
    {
        var result = await _plugin.DetectAsync(S(0xFF, 0xD8, 0xFF, 0xE0), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("jpeg");
        await Assert.That(result.MimeType).IsEqualTo("image/jpeg");
    }

    [Test]
    public async Task Detects_png()
    {
        var result = await _plugin.DetectAsync(
            S(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D),
            CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("png");
        await Assert.That(result.MimeType).IsEqualTo("image/png");
    }

    [Test]
    public async Task Detects_gif()
    {
        var result = await _plugin.DetectAsync(S(0x47, 0x49, 0x46, 0x38, 0x39, 0x61), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("gif");
    }

    [Test]
    public async Task Detects_bmp()
    {
        var result = await _plugin.DetectAsync(S(0x42, 0x4D, 0x36, 0x00), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("bmp");
    }

    [Test]
    public async Task Detects_tiff_little_endian()
    {
        var result = await _plugin.DetectAsync(S(0x49, 0x49, 0x2A, 0x00), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("tiff");
    }

    [Test]
    public async Task Detects_tiff_big_endian()
    {
        var result = await _plugin.DetectAsync(S(0x4D, 0x4D, 0x00, 0x2A), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("tiff");
    }

    [Test]
    public async Task Detects_webp()
    {
        var header = new byte[12];
        header[0] = 0x52; header[1] = 0x49; header[2] = 0x46; header[3] = 0x46; // RIFF
        header[8] = 0x57; header[9] = 0x45; header[10] = 0x42; header[11] = 0x50; // WEBP
        var result = await _plugin.DetectAsync(new MemoryStream(header), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("webp");
    }

    [Test]
    public async Task Detects_heic_isobmff()
    {
        var header = new byte[12];
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70; // ftyp
        header[8] = 0x68; header[9] = 0x65; header[10] = 0x69; header[11] = 0x63; // heic
        var result = await _plugin.DetectAsync(new MemoryStream(header), CancellationToken.None);
        Assert.NotNull(result);
        await Assert.That(result!.FormatId).IsEqualTo("heif");
        await Assert.That(result.MimeType).IsEqualTo("image/heic");
    }

    [Test]
    public async Task Returns_null_for_unknown_format()
    {
        var result = await _plugin.DetectAsync(S(0x00, 0x01, 0x02, 0x03), CancellationToken.None);
        Assert.Null(result);
    }

    [Test]
    public async Task Returns_null_for_too_short_stream()
    {
        var result = await _plugin.DetectAsync(S(0xFF, 0xD8), CancellationToken.None);
        Assert.Null(result);
    }

    [Test]
    public async Task Resets_stream_position_after_detection()
    {
        var stream = S(0xFF, 0xD8, 0xFF, 0xE0);
        stream.Position = 0;
        await _plugin.DetectAsync(stream, CancellationToken.None);
        await Assert.That(stream.Position).IsEqualTo(0);
    }
}