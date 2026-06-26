using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class AvifFormatHandler : IFormatHandler
{
    public string FormatId => "avif";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "AVIF",
            MimeType          = "image/avif",
            Capabilities      = ["image-pixels", "metadata-carrier"]
        };
        return ValueTask.FromResult(doc);
    }
}
