using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ArtifactView.Core.Models;

namespace ArtifactView.App.Converters;

[ValueConversion(typeof(MediaEntityRow), typeof(BitmapImage))]
public sealed class FilmstripImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MediaEntityRow row) return null;
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
