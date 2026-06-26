using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class HeifFormatHandler : IFormatHandler
{
    public string FormatId => "heif";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "HEIC / HEIF",
            MimeType          = "image/heic",
            Capabilities      = ["image-pixels", "metadata-carrier", "embedded-preview"]
        };
        return ValueTask.FromResult(doc);
    }
}
