namespace ArtifactView.Core.Models;

public sealed class FieldCandidate
{
    public required string FieldName { get; init; }
    public required string SourceType { get; init; }
    public string? SourceId { get; init; }
    public string? RawValue { get; init; }
    public string? NormalizedValue { get; init; }
    public ConfidenceScore Confidence { get; init; } = ConfidenceScore.Unknown;
}
