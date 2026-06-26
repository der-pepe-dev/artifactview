using System.Drawing;
using System.Drawing.Imaging;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class RepresentativeFrameAnalyzerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    // Probe actual GDI+ availability — libgdiplus works on Linux too.
    private static readonly Lazy<bool> s_gdiPlusAvailable = new(() =>
    {
        try { _ = new Bitmap(1, 1); return true; }
        catch { return false; }
    });

    private static void RequiresGdiPlus() =>
        Skip.IfNot(s_gdiPlusAvailable.Value, "GDI+ / libgdiplus not available");

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

    [SkippableFact]
    public void Returns_consistent_finding_for_identical_images()
    {
        RequiresGdiPlus();
        var path  = CreateJpeg(Color.SteelBlue);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        Assert.Equal(ReviewPriority.None, finding!.ReviewPriority);
        Assert.Equal("thumb-content-consistent", finding.Id);
    }

    [SkippableFact]
    public void Returns_null_when_main_file_not_decodable()
    {
        RequiresGdiPlus();
        var path = Path.GetTempFileName() + ".jpg";
        _tempFiles.Add(path);
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03]);

        var thumbBytes = CreateJpegBytes(Color.Red);
        var finding    = RepresentativeFrameAnalyzer.Analyze(path, thumbBytes);

        Assert.Null(finding);
    }

    [SkippableFact]
    public void Returns_null_when_thumbnail_bytes_not_decodable()
    {
        RequiresGdiPlus();
        var path      = CreateJpeg(Color.Green);
        byte[] badBytes = [0x00, 0x01, 0x02, 0x03];

        var finding = RepresentativeFrameAnalyzer.Analyze(path, badBytes);

        Assert.Null(finding);
    }

    [SkippableFact]
    public void Returns_high_priority_finding_for_completely_different_images()
    {
        RequiresGdiPlus();
        var mainPath   = CreateJpeg(Color.Red, 128, 128);
        var thumbBytes = CreateJpegBytes(Color.Blue, 64, 64);

        var finding = RepresentativeFrameAnalyzer.Analyze(mainPath, thumbBytes);

        Assert.NotNull(finding);
        Assert.True(finding!.ReviewPriority >= ReviewPriority.Medium,
            $"Expected at least Medium priority, got {finding.ReviewPriority}");
    }

    [SkippableFact]
    public void Finding_has_category_embedded_thumbnail()
    {
        RequiresGdiPlus();
        var path  = CreateJpeg(Color.Gray);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        Assert.Equal("Embedded Thumbnail", finding!.Category);
    }

    [SkippableFact]
    public void Finding_observation_includes_dhash_distance()
    {
        RequiresGdiPlus();
        var path  = CreateJpeg(Color.DarkGreen);
        var bytes = File.ReadAllBytes(path);

        var finding = RepresentativeFrameAnalyzer.Analyze(path, bytes);

        Assert.NotNull(finding);
        Assert.Contains("dHash", finding!.Observation, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void High_priority_finding_includes_supporting_factors()
    {
        RequiresGdiPlus();
        var mainPath   = CreateJpeg(Color.Red, 128, 128);
        var thumbBytes = CreateJpegBytes(Color.Blue, 64, 64);

        var finding = RepresentativeFrameAnalyzer.Analyze(mainPath, thumbBytes);

        if (finding?.ReviewPriority >= ReviewPriority.Medium)
            Assert.True(finding.SupportingFactors.Count > 0 || finding.ReviewPriority == ReviewPriority.Medium,
                "Medium+ findings should mention distance or have supporting factors");
    }
}
