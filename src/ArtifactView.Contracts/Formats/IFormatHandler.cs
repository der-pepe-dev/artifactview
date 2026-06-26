namespace ArtifactView.Contracts.Formats;

public interface IFormatHandler
{
    string FormatId { get; }
    ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken);
}
