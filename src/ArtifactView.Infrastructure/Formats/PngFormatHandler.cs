using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class PngFormatHandler : IFormatHandler
{
    public string FormatId => "png";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "PNG",
            MimeType          = "image/png",
            Capabilities      = ["image-pixels", "metadata-carrier"]
        };
        return ValueTask.FromResult(doc);
    }
}
