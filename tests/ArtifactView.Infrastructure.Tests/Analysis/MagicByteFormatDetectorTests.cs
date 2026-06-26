using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class MagicByteFormatDetectorTests : IDisposable
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
    public void Jpeg_Detected()
    {
        var path = TempFile(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        Assert.Equal(FormatId.Jpeg, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Png_Detected()
    {
        var path = TempFile(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        Assert.Equal(FormatId.Png, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Gif_Detected()
    {
        var path = TempFile(0x47, 0x49, 0x46, 0x38, 0x39, 0x61);
        Assert.Equal(FormatId.Gif, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Bmp_Detected()
    {
        var path = TempFile(0x42, 0x4D, 0x00, 0x00, 0x00, 0x00);
        Assert.Equal(FormatId.Bmp, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Tiff_LittleEndian_Detected()
    {
        var path = TempFile(0x49, 0x49, 0x2A, 0x00);
        Assert.Equal(FormatId.Tiff, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Tiff_BigEndian_Detected()
    {
        var path = TempFile(0x4D, 0x4D, 0x00, 0x2A);
        Assert.Equal(FormatId.Tiff, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void WebP_Detected()
    {
        // RIFF....WEBP
        var path = TempFile(0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00,
                            0x57, 0x45, 0x42, 0x50);
        Assert.Equal(FormatId.WebP, MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void TooSmall_ReturnsNull()
    {
        var path = TempFile(0xFF, 0xD8);
        Assert.Null(MagicByteFormatDetector.Detect(path));
    }

    [Fact]
    public void Unknown_ReturnsNull()
    {
        var path = TempFile(0x00, 0x01, 0x02, 0x03, 0x04, 0x05);
        Assert.Null(MagicByteFormatDetector.Detect(path));
    }

    [Theory]
    [InlineData(".jpg", FormatId.Jpeg)]
    [InlineData(".JPEG", FormatId.Jpeg)]
    [InlineData(".png", FormatId.Png)]
    [InlineData(".gif", FormatId.Gif)]
    [InlineData(".bmp", FormatId.Bmp)]
    [InlineData(".tif", FormatId.Tiff)]
    [InlineData(".webp", FormatId.WebP)]
    [InlineData(".heic", FormatId.Heif)]
    [InlineData(".avif", FormatId.Avif)]
    [InlineData(".xyz", null)]
    public void ExpectedFormatForExtension_MapsCorrectly(string ext, FormatId? expected)
    {
        Assert.Equal(expected, MagicByteFormatDetector.ExpectedFormatForExtension(ext));
    }
}
