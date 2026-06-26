using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class TiffFormatHandler : IFormatHandler
{
    public string FormatId => "tiff";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "TIFF / RAW",
            MimeType          = "image/tiff",
            Capabilities      = ["image-pixels", "metadata-carrier", "multi-page"]
        };
        return ValueTask.FromResult(doc);
    }
}
