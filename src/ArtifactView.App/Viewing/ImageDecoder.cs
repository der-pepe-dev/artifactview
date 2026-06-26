using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArtifactView.App.Viewing;

// Decodes image files on a background thread.
// BitmapCacheOption.OnLoad reads all bytes before the stream closes, and
// Freeze() allows the result to be marshalled safely to the UI thread.
// Exceptions are NOT caught here — let the caller decide how to handle them.
internal static class ImageDecoder
{
    public static BitmapSource Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Decode(stream);
    }

    // Decodes from an in-memory/seekable stream (e.g. carved byte ranges that have no
    // file path) with the same EXIF-orientation handling as the file path overload.
    public static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return ApplyExifOrientation(frame);
    }

    // Decodes the file once and returns both the main frame and the embedded
    // thumbnail in a single pass.  BitmapCacheOption.OnLoad ensures the EXIF
    // APP1 segment is fully parsed — BitmapCacheOption.None often skips it,
    // causing decoder.Thumbnail to return null even when a thumbnail exists.
    // Falls back to the frame-level thumbnail when the decoder-level one is null,
    // covering encoders that store the preview there instead.
    public static (BitmapSource Frame, BitmapSource? Thumbnail) DecodeWithThumbnail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        var orientation = ReadExifOrientation(frame);
        frame.Freeze();

        var orientedFrame = ApplyOrientation(frame, orientation);

        var rawThumb = decoder.Thumbnail
            ?? (decoder.Frames.Count > 0 ? decoder.Frames[0].Thumbnail : null);

        BitmapSource? thumbnail = null;
        if (rawThumb is not null)
        {
            // Copy pixels into a stream-independent BitmapSource so the
            // caller can use CopyPixels() after this method returns.
            var stride = (rawThumb.PixelWidth * rawThumb.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * rawThumb.PixelHeight];
            rawThumb.CopyPixels(pixels, stride, 0);

            thumbnail = BitmapSource.Create(
                rawThumb.PixelWidth, rawThumb.PixelHeight,
                rawThumb.DpiX, rawThumb.DpiY,
                rawThumb.Format, rawThumb.Palette,
                pixels, stride);
            thumbnail = ApplyOrientation(thumbnail, orientation);
            thumbnail.Freeze();
        }

        return (orientedFrame, thumbnail);
    }
    // Reads the EXIF orientation tag (1–8) from a BitmapFrame's metadata.
    // Returns 1 (normal) when the tag is absent or unreadable.
    internal static int ReadExifOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata)
                return 1;

            // JPEG stores orientation in APP1/IFD0; TIFF/RAW in root IFD.
            var val = metadata.GetQuery("/app1/ifd/{ushort=274}")
                   ?? metadata.GetQuery("/ifd/{ushort=274}");

            return val switch
            {
                ushort u => u,
                int i    => i,
                _        => 1
            };
        }
        catch { return 1; }
    }

    private static BitmapSource ApplyExifOrientation(BitmapFrame frame)
        => ApplyOrientation(frame, ReadExifOrientation(frame));

    // Applies an EXIF orientation value (1–8) to a BitmapSource via
    // TransformedBitmap.  Returns the source unchanged for orientation 1
    // (normal) or unknown values.  The returned image is always frozen.
    internal static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        if (orientation <= 1 || orientation > 8)
            return source;

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform is null)
            return source;

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }
}

