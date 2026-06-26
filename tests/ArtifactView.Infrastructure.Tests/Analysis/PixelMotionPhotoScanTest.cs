using ArtifactView.Infrastructure.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class PixelMotionPhotoScanTest(ITestOutputHelper output)
{
    private static readonly string TestFile =
        Path.Combine(
            Path.GetDirectoryName(typeof(PixelMotionPhotoScanTest).Assembly.Location)!,
            "..", "..", "..", "..", "..", "tests",
            "PXL_20250805_095133155.RAW-01.COVER.jpg");

    [SkippableFact]
    public void Scan_does_not_throw_on_pixel_motion_photo()
    {
        Skip.If(!File.Exists(TestFile), "Test file not present");

        var ex = Record.Exception(() => JpegEmbeddedArtifactScanner.Scan(TestFile));
        Assert.Null(ex);
    }

    [SkippableFact]
    public void Scan_reports_artifacts_for_pixel_motion_photo()
    {
        Skip.If(!File.Exists(TestFile), "Test file not present");

        var artifacts = JpegEmbeddedArtifactScanner.Scan(TestFile);
        output.WriteLine($"Found {artifacts.Count} artifact(s):");
        foreach (var a in artifacts)
            output.WriteLine($"  [{a.Type}] {a.DisplayName} mime={a.MimeType} extractable={a.IsExtractable} offset={a.Offset} len={a.Length}");

        Assert.NotEmpty(artifacts);
    }

    [SkippableFact]
    public void ExtractPayload_does_not_throw_for_each_extractable_artifact()
    {
        Skip.If(!File.Exists(TestFile), "Test file not present");

        var artifacts = JpegEmbeddedArtifactScanner.Scan(TestFile);
        foreach (var a in artifacts.Where(x => x.IsExtractable))
        {
            output.WriteLine($"Extracting [{a.Type}] {a.DisplayName} len={a.Length}");
            var ex = Record.Exception(() => JpegEmbeddedArtifactScanner.ExtractPayload(TestFile, a));
            Assert.Null(ex);
        }
    }
}
