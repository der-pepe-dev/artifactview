using System.IO;
using ArtifactView.Infrastructure.Analysis;
using System.Threading.Tasks;

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

    [Test]
    public async Task Jpeg_Detected()
    {
        var path = TempFile(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Jpeg);
    }

    [Test]
    public async Task Png_Detected()
    {
        var path = TempFile(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Png);
    }

    [Test]
    public async Task Gif_Detected()
    {
        var path = TempFile(0x47, 0x49, 0x46, 0x38, 0x39, 0x61);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Gif);
    }

    [Test]
    public async Task Bmp_Detected()
    {
        var path = TempFile(0x42, 0x4D, 0x00, 0x00, 0x00, 0x00);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Bmp);
    }

    [Test]
    public async Task Tiff_LittleEndian_Detected()
    {
        var path = TempFile(0x49, 0x49, 0x2A, 0x00);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Tiff);
    }

    [Test]
    public async Task Tiff_BigEndian_Detected()
    {
        var path = TempFile(0x4D, 0x4D, 0x00, 0x2A);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.Tiff);
    }

    [Test]
    public async Task WebP_Detected()
    {
        // RIFF....WEBP
        var path = TempFile(0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00,
                            0x57, 0x45, 0x42, 0x50);
        await Assert.That(MagicByteFormatDetector.Detect(path)).IsEqualTo(FormatId.WebP);
    }

    [Test]
    public void TooSmall_ReturnsNull()
    {
        var path = TempFile(0xFF, 0xD8);
        Assert.Null(MagicByteFormatDetector.Detect(path));
    }

    [Test]
    public void Unknown_ReturnsNull()
    {
        var path = TempFile(0x00, 0x01, 0x02, 0x03, 0x04, 0x05);
        Assert.Null(MagicByteFormatDetector.Detect(path));
    }

    [Test]
    [Arguments(".jpg", FormatId.Jpeg)]
    [Arguments(".JPEG", FormatId.Jpeg)]
    [Arguments(".png", FormatId.Png)]
    [Arguments(".gif", FormatId.Gif)]
    [Arguments(".bmp", FormatId.Bmp)]
    [Arguments(".tif", FormatId.Tiff)]
    [Arguments(".webp", FormatId.WebP)]
    [Arguments(".heic", FormatId.Heif)]
    [Arguments(".avif", FormatId.Avif)]
    [Arguments(".xyz", null)]
    public async Task ExpectedFormatForExtension_MapsCorrectly(string ext, FormatId? expected)
    {
        await Assert.That(MagicByteFormatDetector.ExpectedFormatForExtension(ext)).IsEqualTo(expected);
    }
}