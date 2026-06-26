using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class WebPFormatHandler : IFormatHandler
{
    public string FormatId => "webp";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "WebP",
            MimeType          = "image/webp",
            Capabilities      = ["image-pixels", "metadata-carrier"]
        };
        return ValueTask.FromResult(doc);
    }
}
