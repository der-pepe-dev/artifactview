using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ArtifactView.App.ViewModels;

namespace ArtifactView.App.Views;

public partial class PluginsWindow : Window
{
    public PluginsWindow(PluginsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

// Converts a string[] of capability names into a comma-separated display string.
public sealed class CapabilitiesConverter : IValueConverter
{
    public static readonly CapabilitiesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string[] caps && caps.Length > 0
            ? string.Join(", ", caps)
            : "no capabilities declared";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
