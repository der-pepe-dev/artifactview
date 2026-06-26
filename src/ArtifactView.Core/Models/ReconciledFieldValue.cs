namespace ArtifactView.Core.Models;

public sealed class ReconciledFieldValue
{
    public required string FieldName { get; init; }
    public string? PreferredValue { get; init; }

    // SourceType of the candidate that won the reconciliation (null when Ambiguous).
    public string? SourceUsed { get; init; }

    public required MergeStatus Status { get; init; }
    public ConfidenceScore Confidence { get; init; } = ConfidenceScore.Unknown;
    public IReadOnlyList<FieldCandidate> Candidates { get; init; } = [];
}
