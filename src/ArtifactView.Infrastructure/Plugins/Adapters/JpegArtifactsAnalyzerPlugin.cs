using ArtifactView.Contracts.Analysis;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.Infrastructure.Plugins.Adapters;

public sealed class JpegArtifactsAnalyzerPlugin : IAnalyzer
{
    public string Id          => "core.analyzer.jpeg-artifacts";
    public string DisplayName => "JPEG embedded artifact scanner";
    public int    CostHint    => 40;

    public bool Supports(IAnalyzerContext context)
    {
        var ext = Path.GetExtension(context.ItemId);
        return ext.Equals(".jpg",  StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IReadOnlyList<Finding>> AnalyzeAsync(
        IAnalyzerContext context, CancellationToken cancellationToken)
    {
        var artifacts = JpegEmbeddedArtifactScanner.Scan(context.ItemId);

        if (artifacts.Count == 0)
            return ValueTask.FromResult<IReadOnlyList<Finding>>([]);

        var findings = new List<Finding>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            var observation = artifact.IsExtractable
                ? $"Embedded artifact detected: {artifact.DisplayName} ({artifact.Length:N0} bytes at offset {artifact.Offset:N0})"
                : $"Embedded artifact declared but not located: {artifact.DisplayName}";

            var interpretation = ArtifactInterpretation(artifact);

            findings.Add(new Finding
            {
                Id                      = $"jpeg-artifact-{artifact.Id}",
                Category                = "Embedded Artifacts",
                Observation             = observation,
                Interpretation          = interpretation,
                ObservationConfidence   = artifact.ParseConfidence,
                InterpretationConfidence = artifact.ParseConfidence,
                ReviewPriority          = artifact.Type == EmbeddedArtifactType.Unknown
                    ? ReviewPriority.Low
                    : ReviewPriority.None,
                Provenance = Id
            });
        }

        return ValueTask.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static string? ArtifactInterpretation(EmbeddedArtifact artifact) => artifact.Type switch
    {
        EmbeddedArtifactType.ExifThumbnail    => "EXIF thumbnail consistent with camera-embedded preview.",
        EmbeddedArtifactType.MotionPhotoVideo => "Motion photo video consistent with Google/Samsung live photo capture.",
        EmbeddedArtifactType.DepthMap         => "Depth map consistent with dual-lens or computational photography pipeline.",
        EmbeddedArtifactType.GainMap          => "HDR gain map consistent with HDR capture or editing workflow.",
        EmbeddedArtifactType.SecondaryImage   => "Secondary JPEG image; possibly a wide-angle, thumbnail, or auxiliary capture.",
        EmbeddedArtifactType.Unknown or
        EmbeddedArtifactType.UnknownTrailer   => "Unrecognised trailing payload. May indicate appended data or a non-standard container. Review recommended.",
        _                                     => null
    };
}
