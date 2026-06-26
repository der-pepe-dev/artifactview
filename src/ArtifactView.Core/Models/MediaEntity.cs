namespace ArtifactView.Core.Models;

public sealed class MediaEntity
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
    public required string LogicalPath { get; init; }
    public required PresenceState PresenceState { get; init; }
    public required MediaKind MediaKind { get; init; }
    public List<EvidenceContributor> Contributors { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<EmbeddedArtifact> EmbeddedArtifacts { get; } = [];
}
