using ArtifactView.Infrastructure.Formats;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Formats;

public sealed class FormatHandlerTests
{
    private static Stream EmptyStream() => new MemoryStream([]);

    [Test]
    public async Task Jpeg_handler_returns_correct_format()
    {
        var handler = new JpegFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("jpeg");
        await Assert.That(doc.DisplayFormatName).IsEqualTo("JPEG");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
        await Assert.That(doc.Capabilities).Contains("metadata-carrier");
        await Assert.That(doc.Capabilities).Contains("embedded-preview");
    }

    [Test]
    public async Task Png_handler_returns_correct_format()
    {
        var handler = new PngFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("png");
        await Assert.That(doc.DisplayFormatName).IsEqualTo("PNG");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
        await Assert.That(doc.Capabilities).Contains("metadata-carrier");
    }

    [Test]
    public async Task Tiff_handler_returns_correct_format()
    {
        var handler = new TiffFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("tiff");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
        await Assert.That(doc.Capabilities).Contains("multi-page");
    }

    [Test]
    public async Task WebP_handler_returns_correct_format()
    {
        var handler = new WebPFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("webp");
        await Assert.That(doc.DisplayFormatName).IsEqualTo("WebP");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
    }

    [Test]
    public async Task Gif_handler_returns_multi_page_capability()
    {
        var handler = new GifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("gif");
        await Assert.That(doc.Capabilities).Contains("multi-page");
    }

    [Test]
    public async Task Bmp_handler_returns_correct_format()
    {
        var handler = new BmpFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("bmp");
        await Assert.That(doc.DisplayFormatName).IsEqualTo("BMP");
    }

    [Test]
    public async Task Heif_handler_returns_correct_format()
    {
        var handler = new HeifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("heif");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
        await Assert.That(doc.Capabilities).Contains("embedded-preview");
    }

    [Test]
    public async Task Avif_handler_returns_correct_format()
    {
        var handler = new AvifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("avif");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
    }

    [Test]
    public async Task IsoBmff_detects_heic_brand()
    {
        // ftyp box: 4-byte size + "ftyp" + brand "heic"
        var header = new byte[12];
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70; // "ftyp"
        header[8] = 0x68; header[9] = 0x65; header[10] = 0x69; header[11] = 0x63; // "heic"

        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(new MemoryStream(header), CancellationToken.None);

        await Assert.That(handler.FormatId).IsEqualTo("isobmff");
        await Assert.That(doc.DisplayFormatName).IsEqualTo("HEIC");
        await Assert.That(doc.Capabilities).Contains("image-pixels");
    }

    [Test]
    public async Task IsoBmff_detects_avif_brand()
    {
        var header = new byte[12];
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70; // "ftyp"
        header[8] = 0x61; header[9] = 0x76; header[10] = 0x69; header[11] = 0x66; // "avif"

        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(new MemoryStream(header), CancellationToken.None);

        await Assert.That(doc.DisplayFormatName).IsEqualTo("AVIF");
    }

    [Test]
    public async Task IsoBmff_falls_back_for_unknown_brand()
    {
        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        await Assert.That(doc.DisplayFormatName).IsEqualTo("ISO Base Media");
    }
}