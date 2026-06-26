using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;

namespace ArtifactView.App.ViewModels;

// Shared logic for converting EmbeddedArtifact scan results into Finding objects.
// Used by both FindingsViewModel (slow path) and ShellViewModel.EnrichRowAsync.
internal static class EmbeddedArtifactFindingsBuilder
{
    internal static void AddFindings(IReadOnlyList<EmbeddedArtifact> artifacts, List<Finding> findings)
    {
        foreach (var artifact in artifacts)
        {
            var sizeHint = artifact.Length is > 0
                ? $" ({artifact.Length.Value:N0} bytes)"
                : string.Empty;

            findings.Add(new Finding
            {
                Id = $"embedded-{artifact.Type.ToString().ToLowerInvariant()}",
                Category = "Embedded Artifact",
                ReviewPriority = artifact.Type switch
                {
                    EmbeddedArtifactType.DepthMap         => ReviewPriority.Low,
                    EmbeddedArtifactType.GainMap          => ReviewPriority.None,
                    EmbeddedArtifactType.MotionPhotoVideo => ReviewPriority.Low,
                    EmbeddedArtifactType.SecondaryImage   => ReviewPriority.Low,
                    EmbeddedArtifactType.Unknown          => ReviewPriority.Medium,
                    _                                     => ReviewPriority.None
                },
                Observation = $"{artifact.DisplayName}{sizeHint} detected.",
                ObservationConfidence = artifact.ParseConfidence,
                Interpretation = artifact.Type switch
                {
                    EmbeddedArtifactType.DepthMap =>
                        "Depth map present — consistent with a portrait/bokeh-mode capture. "
                        + "Can be extracted from the Reconstruction tab.",
                    EmbeddedArtifactType.GainMap =>
                        "HDR gain map present — consistent with an HDR-capable capture. "
                        + "Used by displays that support adaptive tone mapping.",
                    EmbeddedArtifactType.MotionPhotoVideo =>
                        "Motion photo video embedded after the JPEG — consistent with "
                        + "Google/Samsung motion photo. Can be extracted as MP4.",
                    EmbeddedArtifactType.SecondaryImage =>
                        "Secondary JPEG image embedded after the main image. May be a depth map, "
                        + "alternate exposure, or camera-specific auxiliary data.",
                    EmbeddedArtifactType.Unknown =>
                        "Unrecognised data appended after the JPEG EOI marker. "
                        + "Could be steganographic content, app-specific payload, or file corruption.",
                    _ => null
                },
                InterpretationConfidence = new ConfidenceScore(
                    artifact.Type == EmbeddedArtifactType.Unknown ? 50 : 70)
            });
        }
    }
}
