namespace ArtifactView.Core.Models;

public sealed class Finding
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Observation { get; init; }
    public string? Interpretation { get; init; }
    public ConfidenceScore ObservationConfidence { get; init; } = ConfidenceScore.Unknown;
    public ConfidenceScore InterpretationConfidence { get; init; } = ConfidenceScore.Unknown;
    public ReviewPriority ReviewPriority { get; init; } = ReviewPriority.None;
    public IReadOnlyList<string> SupportingFactors { get; init; } = [];
    public IReadOnlyList<string> ConflictingFactors { get; init; } = [];

    // Source context: analyzer name, file path, or contributor type that produced this finding.
    public string? Provenance { get; init; }
}
