using ArtifactView.Contracts.Processing;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Plugins.Adapters;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Plugins;

public sealed class LoFiReconstructionProcessorPluginTests
{
    private readonly LoFiReconstructionProcessorPlugin _plugin =
        new(new ImageMetadataExtractor());

    private sealed record FakeContext(string ItemId) : IProcessorContext
    {
        public IReadOnlyList<string> AvailableArtifacts => [];
    }

    [Fact]
    public void Plugin_id_is_stable()
        => Assert.Equal("core.processor.lofi-exif-thumb", _plugin.Id);

    [Fact]
    public void Plugin_is_evidence_safe()
        => Assert.True(_plugin.IsEvidenceSafe);

    [Fact]
    public void Supports_returns_false_for_nonexistent_file()
        => Assert.False(_plugin.Supports(new FakeContext("/nonexistent/path/photo.jpg")));

    [Fact]
    public void Supports_returns_false_for_wrong_extension()
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try   { Assert.False(_plugin.Supports(new FakeContext(path))); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProcessAsync_returns_failed_for_nonexistent_file()
    {
        var result = await _plugin.ProcessAsync(
            new FakeContext("/nonexistent/path/photo.jpg"), CancellationToken.None);
        Assert.Equal("failed", result.ResultKind);
        Assert.Equal("exif-thumbnail", result.ArtifactId);
        Assert.Null(result.OutputPath);
    }

    [Fact]
    public async Task ProcessAsync_returns_failed_for_jpeg_without_thumbnail()
    {
        // Minimal valid JPEG (SOI + EOI) — no IFD1 thumbnail.
        var path = Path.GetTempFileName() + ".jpg";
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        try
        {
            var result = await _plugin.ProcessAsync(new FakeContext(path), CancellationToken.None);
            Assert.Equal("failed", result.ResultKind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProcessAsync_respects_cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _plugin.ProcessAsync(new FakeContext("any.jpg"), cts.Token).AsTask());
    }

    [SkippableFact]
    public async Task ProcessAsync_produces_png_from_jpeg_with_thumbnail()
    {
        // Requires a real JPEG with EXIF thumbnail. Skip if not found.
        var testJpeg = Environment.GetEnvironmentVariable("ARTIFACTVIEW_TEST_JPEG_WITH_THUMB");
        Skip.If(string.IsNullOrEmpty(testJpeg) || !File.Exists(testJpeg),
            "Set ARTIFACTVIEW_TEST_JPEG_WITH_THUMB to a JPEG with an embedded thumbnail.");

        var result = await _plugin.ProcessAsync(new FakeContext(testJpeg!), CancellationToken.None);
        Assert.Equal("lofi-reconstruction", result.ResultKind);
        Assert.Equal("exif-thumbnail", result.ArtifactId);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith("__lofi_reconstruction__exifthumb.png", result.OutputPath);
    }
}
