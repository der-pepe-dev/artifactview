namespace ArtifactView.Core.Models;

public sealed class EvidenceContributor
{
    public required Guid Id { get; init; }
    public required string SourceKind { get; init; }
    public required string Description { get; init; }
    public required double Confidence { get; init; }
    public string? SourcePath { get; init; }
}
