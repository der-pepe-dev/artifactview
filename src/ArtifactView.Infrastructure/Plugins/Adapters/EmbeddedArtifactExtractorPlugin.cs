using ArtifactView.Contracts.Processing;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

// Headless extraction of all embedded artifacts from a JPEG file.
// Writes exact byte copies to a temp directory — no re-encoding.
// Only JPEG/PNG/MP4/binary payloads; no lossless re-encode needed because
// these are exact binary extractions (CLAUDE.md: exact extractions may
// preserve original format).
public sealed class EmbeddedArtifactExtractorPlugin : IProcessorPlugin
{
    private static readonly HashSet<string> s_jpegExts =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    public string Id             => "core.processor.embedded-artifact-extractor";
    public string DisplayName    => "Embedded artifact extractor";
    public bool   IsEvidenceSafe => true;

    public bool Supports(IProcessorContext context)
    {
        if (!s_jpegExts.Contains(Path.GetExtension(context.ItemId))) return false;
        if (!File.Exists(context.ItemId)) return false;
        try
        {
            return JpegEmbeddedArtifactScanner.Scan(context.ItemId)
                .Any(a => a.IsExtractable);
        }
        catch { return false; }
    }

    public ValueTask<ProcessorResult> ProcessAsync(
        IProcessorContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<EmbeddedArtifact> artifacts;
        try
        {
            artifacts = JpegEmbeddedArtifactScanner.Scan(context.ItemId)
                .Where(a => a.IsExtractable).ToList();
        }
        catch
        {
            return ValueTask.FromResult(new ProcessorResult { ResultKind = "scan-failed" });
        }

        if (artifacts.Count == 0)
            return ValueTask.FromResult(new ProcessorResult { ResultKind = "no-extractable-artifacts" });

        var baseName  = Path.GetFileNameWithoutExtension(context.ItemId);
        var outputDir = Path.Combine(Path.GetTempPath(), "ArtifactView", "extracted-artifacts");
        Directory.CreateDirectory(outputDir);

        var paths = new List<string>(artifacts.Count);

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = JpegEmbeddedArtifactScanner.ExtractPayload(context.ItemId, artifact);
            if (payload is null || payload.Length == 0) continue;

            var ext      = ExtensionFor(artifact.MimeType);
            var suffix   = SuffixFor(artifact.Type);
            var outPath  = Path.Combine(outputDir, $"{baseName}__artifact__{suffix}{ext}");

            File.WriteAllBytes(outPath, payload);
            paths.Add(outPath);
        }

        if (paths.Count == 0)
            return ValueTask.FromResult(new ProcessorResult { ResultKind = "extraction-failed" });

        return ValueTask.FromResult(new ProcessorResult
        {
            ResultKind  = "artifact-extraction",
            ArtifactId  = artifacts.Count == 1 ? artifacts[0].Type.ToString().ToLowerInvariant() : null,
            OutputPath  = paths[0],
            OutputPaths = paths
        });
    }

    private static string ExtensionFor(string? mime) => mime switch
    {
        "video/mp4"  => ".mp4",
        "image/jpeg" => ".jpg",
        "image/png"  => ".png",
        _            => ".bin"
    };

    private static string SuffixFor(EmbeddedArtifactType type) => type switch
    {
        EmbeddedArtifactType.MotionPhotoVideo => "motion_photo",
        EmbeddedArtifactType.DepthMap         => "depth_map",
        EmbeddedArtifactType.GainMap          => "gain_map",
        EmbeddedArtifactType.SecondaryImage   => "secondary_image",
        EmbeddedArtifactType.ExifThumbnail    => "exif_thumbnail",
        _                                     => "unknown_trailer"
    };
}
