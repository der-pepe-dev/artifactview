namespace ArtifactView.Contracts.Formats;

public sealed class FormatDetectionResult
{
    public required string FormatId { get; init; }
    public required string Family { get; init; }
    public string? MimeType { get; init; }
}
