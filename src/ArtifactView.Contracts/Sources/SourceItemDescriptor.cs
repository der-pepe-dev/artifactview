namespace ArtifactView.Contracts.Sources;

public sealed class SourceItemDescriptor
{
    public required string ItemId { get; init; }
    public required string DisplayName { get; init; }
    public required string LogicalPath { get; init; }
    public long? Size { get; init; }
    public string? Extension { get; init; }
    public bool IsDirectory { get; init; }
}

