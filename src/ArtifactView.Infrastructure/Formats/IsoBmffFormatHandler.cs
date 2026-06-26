using ArtifactView.Contracts.Formats;

namespace ArtifactView.Infrastructure.Formats;

public sealed class IsoBmffFormatHandler : IFormatHandler
{
    public string FormatId => "isobmff";

    public ValueTask<IMediaDocument> OpenAsync(Stream stream, CancellationToken cancellationToken)
    {
        var (displayName, mime, caps) = DetectSubtype(stream);
        IMediaDocument doc = new MediaDocument
        {
            DisplayFormatName = displayName,
            MimeType          = mime,
            Capabilities      = caps
        };
        return ValueTask.FromResult(doc);
    }

    private static (string DisplayName, string Mime, string[] Caps) DetectSubtype(Stream stream)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            var pos  = stream.Position;
            var read = stream.Read(header);
            stream.Position = pos;

            if (read >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
            {
                var brand = System.Text.Encoding.ASCII.GetString(header[8..12]);
                return brand switch
                {
                    "heic" or "heix" or "mif1" => ("HEIC",  "image/heic",   ["image-pixels", "metadata-carrier", "embedded-preview"]),
                    "avif" or "avis"           => ("AVIF",  "image/avif",   ["image-pixels", "metadata-carrier"]),
                    "mp41" or "mp42" or "isom" => ("MP4",   "video/mp4",    ["video", "metadata-carrier"]),
                    "qt  "                     => ("MOV",   "video/quicktime", ["video", "metadata-carrier"]),
                    _                          => ("ISO Base Media", "video/mp4", ["metadata-carrier"])
                };
            }
        }
        catch { }

        return ("ISO Base Media", "video/mp4", ["metadata-carrier"]);
    }
}
