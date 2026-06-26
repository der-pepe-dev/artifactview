using System.IO;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Tests.Analysis;

public sealed class PixelMotionPhotoScanTest
{
    private static readonly string TestFile =
        Path.Combine(
            Path.GetDirectoryName(typeof(PixelMotionPhotoScanTest).Assembly.Location)!,
            "..", "..", "..", "..", "..", "tests",
            "PXL_20250805_095133155.RAW-01.COVER.jpg");

    [Test]
    public async Task Scan_does_not_throw_on_pixel_motion_photo()
    {
        Skip.When(!File.Exists(TestFile), "Test file not present");

        await Assert.That(() => JpegEmbeddedArtifactScanner.Scan(TestFile)).ThrowsNothing();
    }

    [Test]
    public async Task Scan_reports_artifacts_for_pixel_motion_photo()
    {
        Skip.When(!File.Exists(TestFile), "Test file not present");

        var artifacts = JpegEmbeddedArtifactScanner.Scan(TestFile);
        Console.WriteLine($"Found {artifacts.Count} artifact(s):");
        foreach (var a in artifacts)
            Console.WriteLine($"  [{a.Type}] {a.DisplayName} mime={a.MimeType} extractable={a.IsExtractable} offset={a.Offset} len={a.Length}");

        await Assert.That(artifacts).IsNotEmpty();
    }

    [Test]
    public async Task ExtractPayload_does_not_throw_for_each_extractable_artifact()
    {
        Skip.When(!File.Exists(TestFile), "Test file not present");

        var artifacts = JpegEmbeddedArtifactScanner.Scan(TestFile);
        foreach (var a in artifacts.Where(x => x.IsExtractable))
        {
            Console.WriteLine($"Extracting [{a.Type}] {a.DisplayName} len={a.Length}");
            await Assert.That(() => JpegEmbeddedArtifactScanner.ExtractPayload(TestFile, a)).ThrowsNothing();
        }
    }
}
