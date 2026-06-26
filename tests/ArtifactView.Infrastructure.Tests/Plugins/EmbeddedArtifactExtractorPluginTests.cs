using ArtifactView.Contracts.Processing;
using ArtifactView.Infrastructure.Plugins.Adapters;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Plugins;

public sealed class EmbeddedArtifactExtractorPluginTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly EmbeddedArtifactExtractorPlugin _plugin = new();

    private string WriteTempJpeg(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    // Minimal JPEG: SOI + SOS(len=2) + EOI + optional trailing bytes.
    private static byte[] MinimalJpeg(byte[]? trailing = null)
    {
        byte[] body = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9];
        if (trailing is null) return body;
        return [..body, ..trailing];
    }

    // Minimal ISO BMFF ftyp box (looks like an MP4 container start).
    private static byte[] FtypBox() =>
    [
        0x00, 0x00, 0x00, 0x14,              // box size = 20
        0x66, 0x74, 0x79, 0x70,              // "ftyp"
        0x6D, 0x70, 0x34, 0x32,              // "mp42"
        0x00, 0x00, 0x00, 0x00,              // minor version
        0x6D, 0x70, 0x34, 0x32,              // compatible brand
    ];

    private static SimpleContext Ctx(string path) =>
        new(path, []);

    private sealed record SimpleContext(string ItemId, IReadOnlyList<string> AvailableArtifacts)
        : IProcessorContext;

    // ── Supports ────────────────────────────────────────────────────────────

    [Fact]
    public void Supports_returns_false_for_non_jpeg_extension()
    {
        Assert.False(_plugin.Supports(new SimpleContext("/some/file.png", [])));
    }

    [Fact]
    public void Supports_returns_false_for_missing_file()
    {
        Assert.False(_plugin.Supports(new SimpleContext("/nonexistent.jpg", [])));
    }

    [Fact]
    public void Supports_returns_false_for_jpeg_with_no_artifacts()
    {
        var path = WriteTempJpeg(MinimalJpeg());
        Assert.False(_plugin.Supports(Ctx(path)));
    }

    [Fact]
    public void Supports_returns_true_for_jpeg_with_mp4_trailer()
    {
        var path = WriteTempJpeg(MinimalJpeg(FtypBox()));
        Assert.True(_plugin.Supports(Ctx(path)));
    }

    [Fact]
    public void Supports_returns_true_for_jpeg_with_secondary_jpeg_trailer()
    {
        byte[] trailingJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x00, 0x00];
        var path = WriteTempJpeg(MinimalJpeg(trailingJpeg));
        Assert.True(_plugin.Supports(Ctx(path)));
    }

    // ── ProcessAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_returns_no_artifacts_for_clean_jpeg()
    {
        var path   = WriteTempJpeg(MinimalJpeg());
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        Assert.Equal("no-extractable-artifacts", result.ResultKind);
        Assert.Null(result.OutputPath);
        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public async Task ProcessAsync_extracts_mp4_trailer_as_motion_photo()
    {
        var path   = WriteTempJpeg(MinimalJpeg(FtypBox()));
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        Assert.Equal("artifact-extraction", result.ResultKind);
        Assert.NotNull(result.OutputPath);
        Assert.NotEmpty(result.OutputPaths);
        Assert.True(result.OutputPath!.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                 || result.OutputPaths.Any(p => p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProcessAsync_output_file_contains_exact_bytes()
    {
        var ftyp = FtypBox();
        var path = WriteTempJpeg(MinimalJpeg(ftyp));

        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        var written = File.ReadAllBytes(result.OutputPath!);
        Assert.Equal(ftyp, written);

        // Cleanup extracted file.
        File.Delete(result.OutputPath!);
    }

    [Fact]
    public async Task ProcessAsync_output_paths_equal_output_path_for_single_artifact()
    {
        var path   = WriteTempJpeg(MinimalJpeg(FtypBox()));
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        if (result.OutputPaths.Count == 1)
            Assert.Equal(result.OutputPath, result.OutputPaths[0]);

        // Cleanup.
        foreach (var f in result.OutputPaths)
            try { File.Delete(f); } catch { }
    }

    [Fact]
    public void IsEvidenceSafe_is_true()
        => Assert.True(_plugin.IsEvidenceSafe);

    [Fact]
    public void Id_is_stable()
        => Assert.Equal("core.processor.embedded-artifact-extractor", _plugin.Id);
}
