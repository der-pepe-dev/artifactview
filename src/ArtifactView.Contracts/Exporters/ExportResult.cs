namespace ArtifactView.Contracts.Exporters;

public sealed class ExportResult
{
    public required bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public long? BytesWritten { get; init; }
}
