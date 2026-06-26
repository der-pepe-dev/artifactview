using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ArtifactView.Core.Models;

namespace ArtifactView.App.ViewModels;

// UI wrapper for an EmbeddedArtifact that adds an inline preview thumbnail.
// Preview is loaded lazily on the background thread and frozen before
// posting to the UI thread.
public sealed class EmbeddedArtifactRowViewModel : INotifyPropertyChanged
{
    private BitmapSource? _preview;
    private bool          _previewLoaded;

    public EmbeddedArtifactRowViewModel(EmbeddedArtifact artifact)
    {
        Artifact = artifact;
    }

    public EmbeddedArtifact Artifact { get; }

    // Forwarded display properties.
    public string         DisplayName     => Artifact.DisplayName;
    public string?        MimeType        => Artifact.MimeType;
    public ConfidenceScore ParseConfidence => Artifact.ParseConfidence;
    public long?          Length          => Artifact.Length;
    public bool           IsExtractable   => Artifact.IsExtractable;

    public string SizeText => Artifact.Length is > 0
        ? $"{Artifact.Length.Value:N0} bytes"
        : string.Empty;

    // Null until LoadPreviewAsync completes.
    public BitmapSource? Preview
    {
        get => _preview;
        private set { _preview = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPreview)); }
    }

    public bool HasPreview => _preview is not null;

    // True for image/* types — video and binary never get a decoded preview.
    public bool CanPreview => Artifact.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    // Loads and caches the preview.  Safe to call multiple times; only runs once.
    public async Task LoadPreviewAsync(Func<EmbeddedArtifact, byte[]?> extractor)
    {
        if (_previewLoaded) return;
        _previewLoaded = true;

        if (!CanPreview) return;

        await Task.Run(() =>
        {
            try
            {
                var bytes = extractor(Artifact);
                if (bytes is null || bytes.Length < 4) return;

                using var ms = new System.IO.MemoryStream(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource    = ms;
                img.DecodePixelWidth = 96; // thumbnail size
                img.CacheOption     = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                Preview = img;
            }
            catch { /* best-effort */ }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
