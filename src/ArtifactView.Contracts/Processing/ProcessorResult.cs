namespace ArtifactView.Contracts.Processing;

public sealed class ProcessorResult
{
    public required string ResultKind { get; init; }
    public string? ArtifactId { get; init; }
    public string? OutputPath { get; init; }

    // Multiple output files (e.g. one per embedded artifact).
    // OutputPath is OutputPaths[0] when both are set.
    public IReadOnlyList<string> OutputPaths { get; init; } = [];
}
