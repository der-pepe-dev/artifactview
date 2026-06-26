using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class JpegFormatHandler : IFormatHandler
{
    public string FormatId => "jpeg";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "JPEG",
            MimeType          = "image/jpeg",
            Capabilities      = ["image-pixels", "metadata-carrier", "embedded-preview"]
        };
        return ValueTask.FromResult(doc);
    }
}
