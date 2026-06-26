using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class GifFormatHandler : IFormatHandler
{
    public string FormatId => "gif";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "GIF",
            MimeType          = "image/gif",
            Capabilities      = ["image-pixels", "multi-page"]
        };
        return ValueTask.FromResult(doc);
    }
}
