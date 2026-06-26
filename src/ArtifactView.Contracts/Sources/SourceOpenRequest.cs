namespace ArtifactView.Contracts.Sources;

public sealed class SourceOpenRequest
{
    public required string Location { get; init; }
    public Dictionary<string, string> Options { get; init; } = [];
}
