using System.IO;
using ArtifactView.Contracts.Processing;
using ArtifactView.Infrastructure.Plugins.Adapters;
using System.Threading.Tasks;

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

    [Test]
    public async Task Supports_returns_false_for_non_jpeg_extension()
    {
        await Assert.That(_plugin.Supports(new SimpleContext("/some/file.png", []))).IsFalse();
    }

    [Test]
    public async Task Supports_returns_false_for_missing_file()
    {
        await Assert.That(_plugin.Supports(new SimpleContext("/nonexistent.jpg", []))).IsFalse();
    }

    [Test]
    public async Task Supports_returns_false_for_jpeg_with_no_artifacts()
    {
        var path = WriteTempJpeg(MinimalJpeg());
        await Assert.That(_plugin.Supports(Ctx(path))).IsFalse();
    }

    [Test]
    public async Task Supports_returns_true_for_jpeg_with_mp4_trailer()
    {
        var path = WriteTempJpeg(MinimalJpeg(FtypBox()));
        await Assert.That(_plugin.Supports(Ctx(path))).IsTrue();
    }

    [Test]
    public async Task Supports_returns_true_for_jpeg_with_secondary_jpeg_trailer()
    {
        byte[] trailingJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x00, 0x00];
        var path = WriteTempJpeg(MinimalJpeg(trailingJpeg));
        await Assert.That(_plugin.Supports(Ctx(path))).IsTrue();
    }

    // ── ProcessAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task ProcessAsync_returns_no_artifacts_for_clean_jpeg()
    {
        var path   = WriteTempJpeg(MinimalJpeg());
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        await Assert.That(result.ResultKind).IsEqualTo("no-extractable-artifacts");
        Assert.Null(result.OutputPath);
        await Assert.That(result.OutputPaths).IsEmpty();
    }

    [Test]
    public async Task ProcessAsync_extracts_mp4_trailer_as_motion_photo()
    {
        var path   = WriteTempJpeg(MinimalJpeg(FtypBox()));
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        await Assert.That(result.ResultKind).IsEqualTo("artifact-extraction");
        Assert.NotNull(result.OutputPath);
        await Assert.That(result.OutputPaths).IsNotEmpty();
        await Assert.That(result.OutputPath!.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                 || result.OutputPaths.Any(p => p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task ProcessAsync_output_file_contains_exact_bytes()
    {
        var ftyp = FtypBox();
        var path = WriteTempJpeg(MinimalJpeg(ftyp));

        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        Assert.NotNull(result.OutputPath);
        await Assert.That(File.Exists(result.OutputPath)).IsTrue();

        var written = File.ReadAllBytes(result.OutputPath!);
        await Assert.That(written).IsEquivalentTo(ftyp);

        // Cleanup extracted file.
        File.Delete(result.OutputPath!);
    }

    [Test]
    public async Task ProcessAsync_output_paths_equal_output_path_for_single_artifact()
    {
        var path   = WriteTempJpeg(MinimalJpeg(FtypBox()));
        var result = await _plugin.ProcessAsync(Ctx(path), CancellationToken.None);

        if (result.OutputPaths.Count == 1)
            await Assert.That(result.OutputPaths[0]).IsEqualTo(result.OutputPath);

        // Cleanup.
        foreach (var f in result.OutputPaths)
            try { File.Delete(f); } catch { }
    }

    [Test]
    public async Task IsEvidenceSafe_is_true()
        => await Assert.That(_plugin.IsEvidenceSafe).IsTrue();

    [Test]
    public async Task Id_is_stable()
        => await Assert.That(_plugin.Id).IsEqualTo("core.processor.embedded-artifact-extractor");
}