namespace ArtifactView.Contracts.Signatures;

public sealed class SignatureMatchResult
{
    public required string ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public required MatchStrength Strength { get; init; }
    public IReadOnlyList<string> SupportingFactors { get; init; } = [];
    public IReadOnlyList<string> ConflictingFactors { get; init; } = [];
    public string? Notes { get; init; }
}
