using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class MediaDocument : IMediaDocument
{
    public required string DisplayFormatName { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string? MimeType { get; init; }
}
