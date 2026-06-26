using System.IO;
using System.Windows.Media.Imaging;

namespace ArtifactView.App.Viewing;

// Extracts the embedded EXIF thumbnail without decoding the full main image.
// The thumbnail pixel data is copied into a self-contained BitmapSource before
// the FileStream closes, so callers can use CopyPixels() at any time afterwards.
internal static class EmbeddedThumbnailDecoder
{
    public static BitmapSource? Extract(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        // BitmapCacheOption.None avoids decoding the large main image.
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);

        var thumb = decoder.Thumbnail;
        if (thumb is null)
            return null;

        // Read the main image's EXIF orientation while the stream is still
        // open so the thumbnail displays with the correct rotation.
        var orientation = decoder.Frames.Count > 0
            ? ImageDecoder.ReadExifOrientation(decoder.Frames[0])
            : 1;

        // Force the thumbnail pixel data into a raw byte array while the stream
        // is still open.  BitmapCacheOption.None makes pixel access lazy, so
        // CopyPixels would fail after the stream closes without this step.
        var stride = (thumb.PixelWidth * thumb.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * thumb.PixelHeight];
        thumb.CopyPixels(pixels, stride, 0);

        // Wrap in a new, stream-independent BitmapSource and freeze it.
        var result = BitmapSource.Create(
            thumb.PixelWidth, thumb.PixelHeight,
            thumb.DpiX, thumb.DpiY,
            thumb.Format, thumb.Palette,
            pixels, stride);
        result = ImageDecoder.ApplyOrientation(result, orientation);
        result.Freeze();
        return result;
    }
}
