using ArtifactView.Infrastructure.Formats;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Formats;

public sealed class FormatHandlerTests
{
    private static Stream EmptyStream() => new MemoryStream([]);

    [Fact]
    public async Task Jpeg_handler_returns_correct_format()
    {
        var handler = new JpegFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("jpeg", handler.FormatId);
        Assert.Equal("JPEG", doc.DisplayFormatName);
        Assert.Contains("image-pixels", doc.Capabilities);
        Assert.Contains("metadata-carrier", doc.Capabilities);
        Assert.Contains("embedded-preview", doc.Capabilities);
    }

    [Fact]
    public async Task Png_handler_returns_correct_format()
    {
        var handler = new PngFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("png", handler.FormatId);
        Assert.Equal("PNG", doc.DisplayFormatName);
        Assert.Contains("image-pixels", doc.Capabilities);
        Assert.Contains("metadata-carrier", doc.Capabilities);
    }

    [Fact]
    public async Task Tiff_handler_returns_correct_format()
    {
        var handler = new TiffFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("tiff", handler.FormatId);
        Assert.Contains("image-pixels", doc.Capabilities);
        Assert.Contains("multi-page", doc.Capabilities);
    }

    [Fact]
    public async Task WebP_handler_returns_correct_format()
    {
        var handler = new WebPFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("webp", handler.FormatId);
        Assert.Equal("WebP", doc.DisplayFormatName);
        Assert.Contains("image-pixels", doc.Capabilities);
    }

    [Fact]
    public async Task Gif_handler_returns_multi_page_capability()
    {
        var handler = new GifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("gif", handler.FormatId);
        Assert.Contains("multi-page", doc.Capabilities);
    }

    [Fact]
    public async Task Bmp_handler_returns_correct_format()
    {
        var handler = new BmpFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("bmp", handler.FormatId);
        Assert.Equal("BMP", doc.DisplayFormatName);
    }

    [Fact]
    public async Task Heif_handler_returns_correct_format()
    {
        var handler = new HeifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("heif", handler.FormatId);
        Assert.Contains("image-pixels", doc.Capabilities);
        Assert.Contains("embedded-preview", doc.Capabilities);
    }

    [Fact]
    public async Task Avif_handler_returns_correct_format()
    {
        var handler = new AvifFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("avif", handler.FormatId);
        Assert.Contains("image-pixels", doc.Capabilities);
    }

    [Fact]
    public async Task IsoBmff_detects_heic_brand()
    {
        // ftyp box: 4-byte size + "ftyp" + brand "heic"
        var header = new byte[12];
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70; // "ftyp"
        header[8] = 0x68; header[9] = 0x65; header[10] = 0x69; header[11] = 0x63; // "heic"

        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(new MemoryStream(header), CancellationToken.None);

        Assert.Equal("isobmff", handler.FormatId);
        Assert.Equal("HEIC", doc.DisplayFormatName);
        Assert.Contains("image-pixels", doc.Capabilities);
    }

    [Fact]
    public async Task IsoBmff_detects_avif_brand()
    {
        var header = new byte[12];
        header[4] = 0x66; header[5] = 0x74; header[6] = 0x79; header[7] = 0x70; // "ftyp"
        header[8] = 0x61; header[9] = 0x76; header[10] = 0x69; header[11] = 0x66; // "avif"

        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(new MemoryStream(header), CancellationToken.None);

        Assert.Equal("AVIF", doc.DisplayFormatName);
    }

    [Fact]
    public async Task IsoBmff_falls_back_for_unknown_brand()
    {
        var handler = new IsoBmffFormatHandler();
        var doc = await handler.OpenAsync(EmptyStream(), CancellationToken.None);

        Assert.Equal("ISO Base Media", doc.DisplayFormatName);
    }
}
