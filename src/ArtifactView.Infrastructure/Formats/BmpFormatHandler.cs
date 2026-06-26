using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class BmpFormatHandler : IFormatHandler
{
    public string FormatId => "bmp";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = "BMP",
            MimeType          = "image/bmp",
            Capabilities      = ["image-pixels"]
        };
        return ValueTask.FromResult(doc);
    }
}
