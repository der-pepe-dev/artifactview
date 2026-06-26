using System.IO;
using ArtifactView.Contracts.Processing;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Plugins.Adapters;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Plugins;

public sealed class LoFiReconstructionProcessorPluginTests
{
    private readonly LoFiReconstructionProcessorPlugin _plugin =
        new(new ImageMetadataExtractor());

    private sealed record FakeContext(string ItemId) : IProcessorContext
    {
        public IReadOnlyList<string> AvailableArtifacts => [];
    }

    [Test]
    public async Task Plugin_id_is_stable()
        => await Assert.That(_plugin.Id).IsEqualTo("core.processor.lofi-exif-thumb");

    [Test]
    public async Task Plugin_is_evidence_safe()
        => await Assert.That(_plugin.IsEvidenceSafe).IsTrue();

    [Test]
    public async Task Supports_returns_false_for_nonexistent_file()
        => await Assert.That(_plugin.Supports(new FakeContext("/nonexistent/path/photo.jpg"))).IsFalse();

    [Test]
    public async Task Supports_returns_false_for_wrong_extension()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try   { await Assert.That(_plugin.Supports(new FakeContext(path))).IsFalse(); }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task ProcessAsync_returns_failed_for_nonexistent_file()
    {
        var result = await _plugin.ProcessAsync(
            new FakeContext("/nonexistent/path/photo.jpg"), CancellationToken.None);
        await Assert.That(result.ResultKind).IsEqualTo("failed");
        await Assert.That(result.ArtifactId).IsEqualTo("exif-thumbnail");
        Assert.Null(result.OutputPath);
    }

    [Test]
    public async Task ProcessAsync_returns_failed_for_jpeg_without_thumbnail()
    {
        // Minimal valid JPEG (SOI + EOI) — no IFD1 thumbnail.
        var path = Path.GetTempFileName() + ".jpg";
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        try
        {
            var result = await _plugin.ProcessAsync(new FakeContext(path), CancellationToken.None);
            await Assert.That(result.ResultKind).IsEqualTo("failed");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task ProcessAsync_respects_cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _plugin.ProcessAsync(new FakeContext("any.jpg"), cts.Token).AsTask());
    }

    [Test]
    public async Task ProcessAsync_produces_png_from_jpeg_with_thumbnail()
    {
        // Requires a real JPEG with EXIF thumbnail. Skip if not found.
        var testJpeg = Environment.GetEnvironmentVariable("ARTIFACTVIEW_TEST_JPEG_WITH_THUMB");
        Skip.When(string.IsNullOrEmpty(testJpeg) || !File.Exists(testJpeg),
            "Set ARTIFACTVIEW_TEST_JPEG_WITH_THUMB to a JPEG with an embedded thumbnail.");

        var result = await _plugin.ProcessAsync(new FakeContext(testJpeg!), CancellationToken.None);
        await Assert.That(result.ResultKind).IsEqualTo("lofi-reconstruction");
        await Assert.That(result.ArtifactId).IsEqualTo("exif-thumbnail");
        Assert.NotNull(result.OutputPath);
        await Assert.That(File.Exists(result.OutputPath)).IsTrue();
        await Assert.That(result.OutputPath).EndsWith("__lofi_reconstruction__exifthumb.png");
    }
}