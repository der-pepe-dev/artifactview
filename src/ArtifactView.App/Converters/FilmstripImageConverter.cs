using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ArtifactView.Core.Models;

namespace ArtifactView.App.Converters;

// Produces the filmstrip cell image. MultiBinding inputs: [0] the MediaEntityRow,
// [1] its background-computed FilmstripThumbnail. Byte-source rows (carved / disk-image /
// deleted) have no host path, so they show the precomputed thumbnail once it arrives; file
// rows decode lazily from LogicalPath.
public sealed class FilmstripImageConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 1 && values[1] is BitmapSource precomputed)
            return precomputed;

        if (values.Length == 0 || values[0] is not MediaEntityRow row) return null;
        var path = row.LogicalPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource         = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelHeight = 60;
            bmp.CreateOptions     = BitmapCreateOptions.DelayCreation;
            bmp.CacheOption       = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
