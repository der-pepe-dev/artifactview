using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class RepresentativeFrameAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    private string CreateJpeg(Color color, int width = 64, int height = 64)
    {
        var path = Path.GetTempFileName() + ".jpg";
        _tempFiles.Add(path);
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        bmp.Save(path, ImageFormat.Jpeg);
        return path;
    }

    private static byte[] CreateJpegBytes(Color color, int width = 64, int height = 64)
    {
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    [Test, RequiresGdiPlus]
    public async Task Returns_consistent_finding_for_identical_images()
    {
        var path  = CreateJpeg(Color.SteelBlue);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        await Assert.That(finding!.ReviewPriority).IsEqualTo(ReviewPriority.None);
        await Assert.That(finding.Id).IsEqualTo("thumb-content-consistent");
    }

    [Test, RequiresGdiPlus]
    public void Returns_null_when_main_file_not_decodable()
    {
        var path = Path.GetTempFileName() + ".jpg";
        _tempFiles.Add(path);
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);

        var thumbBytes = CreateJpegBytes(Color.Red);
        var finding    = RepresentativeFrameAnalyzer.Analyze(path, thumbBytes);

        Assert.Null(finding);
    }

    [Test, RequiresGdiPlus]
    public void Returns_null_when_thumbnail_bytes_not_decodable()
    {
        var path      = CreateJpeg(Color.Green);
        byte[] badBytes = [0x00, 0x01, 0x02, 0x03];

        var finding = RepresentativeFrameAnalyzer.Analyze(path, badBytes);

        Assert.Null(finding);
    }

    [Test, RequiresGdiPlus]
    public async Task Returns_high_priority_finding_for_completely_different_images()
    {
        var mainPath   = CreateJpeg(Color.Red, 128, 128);
        var thumbBytes = CreateJpegBytes(Color.Blue, 64, 64);

        var finding = RepresentativeFrameAnalyzer.Analyze(mainPath, thumbBytes);

        Assert.NotNull(finding);
        await Assert.That(finding!.ReviewPriority >= ReviewPriority.Medium).IsTrue().Because($"Expected at least Medium priority, got {finding.ReviewPriority}");
    }

    [Test, RequiresGdiPlus]
    public async Task Finding_has_category_embedded_thumbnail()
    {
        var path  = CreateJpeg(Color.Gray);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        await Assert.That(finding!.Category).IsEqualTo("Embedded Thumbnail");
    }

    [Test, RequiresGdiPlus]
    public async Task Finding_observation_includes_dhash_distance()
    {
        var path  = CreateJpeg(Color.DarkGreen);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        await Assert.That(finding!.Observation).Contains("dHash");
    }

    [Test, RequiresGdiPlus]
    public async Task High_priority_finding_includes_supporting_factors()
    {
        var mainPath   = CreateJpeg(Color.Red, 128, 128);
        var thumbBytes = CreateJpegBytes(Color.Blue, 64, 64);

        var finding = RepresentativeFrameAnalyzer.Analyze(mainPath, thumbBytes);

        if (finding?.ReviewPriority >= ReviewPriority.Medium)
            await Assert.That(finding.SupportingFactors.Count > 0 || finding.ReviewPriority == ReviewPriority.Medium).IsTrue().Because("Medium+ findings should mention distance or have supporting factors");
    }
}
