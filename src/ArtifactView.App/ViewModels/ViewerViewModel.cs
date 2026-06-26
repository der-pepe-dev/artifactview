using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArtifactView.App.Commands;
using ArtifactView.App.Viewing;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Sources.DiskImage;
using ArtifactView.Infrastructure.ThumbCache;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App.ViewModels;

public sealed class ViewerViewModel : INotifyPropertyChanged
{
    private readonly ILogger<ViewerViewModel> _logger;
    private BitmapSource? _source;
    private bool _isFitMode = true;
    private BitmapScalingMode _scalingMode = BitmapScalingMode.HighQuality;
    private string? _loadError;
    private bool _isGhostPreview;
    private CancellationTokenSource _loadCts = new();

    public ViewerViewModel(ILogger<ViewerViewModel> logger)
    {
        _logger = logger;
        FitToWindowCommand = new RelayCommand(_ => SetFitMode());
        ActualSizeCommand  = new RelayCommand(_ => SetActualSizeMode());
    }

    public BitmapSource? Source
    {
        get => _source;
        private set
        {
            _source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(DimensionsText));
        }
    }

    public bool HasImage => _source is not null;

    // Pixel dimensions of the loaded image, e.g. "4032×3024". Empty when no image is loaded.
    public string DimensionsText =>
        _source is not null ? $"{_source.PixelWidth}\u00d7{_source.PixelHeight}" : string.Empty;

    // True when in fit-to-window mode; false when in 1:1 pixel mode.
    // Used by XAML to toggle between the fit panel and the pan panel.
    public bool IsFitMode
    {
        get => _isFitMode;
        private set { _isFitMode = value; OnPropertyChanged(); }
    }

    public BitmapScalingMode ScalingMode
    {
        get => _scalingMode;
        private set { _scalingMode = value; OnPropertyChanged(); }
    }

    // Non-null when the last load attempt failed — shown in the viewer.
    public string? LoadError
    {
        get => _loadError;
        private set { _loadError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLoadError)); }
    }

    public bool HasLoadError => _loadError is not null;

    // True when the current image is a cached Thumbs.db thumbnail for a ghost file.
    public bool IsGhostPreview
    {
        get => _isGhostPreview;
        private set { _isGhostPreview = value; OnPropertyChanged(); }
    }

    public ICommand FitToWindowCommand { get; }
    public ICommand ActualSizeCommand  { get; }

    public void LoadAsync(MediaEntityRow? row)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        LoadError     = null;
        IsGhostPreview = false;

        var token = _loadCts.Token;

        // Carved artifact: no filesystem path — read the carved byte range from the image.
        if (row is not null && !string.IsNullOrEmpty(row.CarvedImagePath) && row.CarvedLength > 0)
        {
            _ = Task.Run(() => LoadCarvedAsync(row, token), token);
            return;
        }

        // Live file inside a disk image: read its content out of the image via DiscUtils.
        if (row is not null && !string.IsNullOrEmpty(row.DiskImagePath))
        {
            _ = Task.Run(() => LoadDiskImageFileAsync(row, token), token);
            return;
        }

        if (row is null || string.IsNullOrEmpty(row.LogicalPath))
        {
            Source = null;
            return;
        }

        // Ghost file: extract and display the cached Thumbs.db thumbnail.
        if (row.PresenceState == "Ghost")
        {
            _ = Task.Run(() => LoadGhostAsync(row, token), token);
            return;
        }

        if (!File.Exists(row.LogicalPath))
        {
            Source = null;
            return;
        }

        var path = row.LogicalPath;
        _ = Task.Run(() =>
        {
            // Stage 1: EXIF thumbnail as an immediate lo-fi preview.
            // Reads only the first ~64 KB of the file, so this completes in milliseconds
            // even for large RAW files. Shows a blurry placeholder while the full
            // decode is in progress, consistent with the "immediate preview first" rule.
            try
            {
                var preview = EmbeddedThumbnailDecoder.Extract(path);
                if (preview is not null && !token.IsCancellationRequested)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!token.IsCancellationRequested && Source is null)
                            Source = preview;
                    });
                }
            }
            catch { /* Non-fatal: full decode follows regardless */ }

            if (token.IsCancellationRequested)
                return;

            // Stage 2: Full-quality decode — replaces the preview when ready.
            try
            {
                var bmp = ImageDecoder.Decode(path);
                if (token.IsCancellationRequested)
                    return;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    Source    = bmp;
                    LoadError = null;
                });
            }
            catch (OperationCanceledException) { }
            catch (NotSupportedException)
            {
                _logger.LogWarning("Format not supported: {Path}", path);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Source    = null;
                    LoadError = $"Format not supported: {System.IO.Path.GetExtension(path)}";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode image: {Path}", path);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Source    = null;
                    LoadError = $"Could not open file: {ex.Message}";
                });
            }
        }, token);
    }

    // Loads a signature-carved artifact by reading its byte range directly from the
    // source image. Carved artifacts have no filesystem path, only an [offset, +length).
    private void LoadCarvedAsync(MediaEntityRow row, CancellationToken token)
    {
        byte[]? payload = null;
        try
        {
            if (row.CarvedLength > 0 && row.CarvedLength <= int.MaxValue)
            {
                payload = new byte[(int)row.CarvedLength];
                using var fs = new FileStream(row.CarvedImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(row.CarvedOffset, SeekOrigin.Begin);
                fs.ReadExactly(payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read carved bytes: {Name}", row.DisplayName);
        }

        if (token.IsCancellationRequested) return;

        if (payload is null || payload.Length == 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source    = null;
                LoadError = "Carved artifact — bytes unavailable.";
            });
            return;
        }

        try
        {
            using var ms = new MemoryStream(payload);
            // Use the shared decoder so carved photos get EXIF-orientation handling,
            // identical to the normal file path.
            var frame = ImageDecoder.Decode(ms);

            if (token.IsCancellationRequested) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested) return;
                Source    = frame;
                LoadError = null;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode carved artifact: {Name}", row.DisplayName);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source    = null;
                LoadError = "Carved artifact — could not be decoded.";
            });
        }
    }

    // Loads a live file that lives inside a raw disk image by reading its content out of
    // the image via DiscUtils. Deleted disk-image files have no DiskImagePath and fall
    // through to the ghost path instead.
    private void LoadDiskImageFileAsync(MediaEntityRow row, CancellationToken token)
    {
        byte[]? payload = null;
        try
        {
            payload = DiskImagePartitionReader.ReadFileBytes(
                row.DiskImagePath, row.DiskImagePartitionIndex,
                row.DiskImageInternalPath, row.DiskImageFilesystem);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read disk-image file: {Name}", row.DisplayName);
        }

        if (token.IsCancellationRequested) return;

        if (payload is null || payload.Length == 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source    = null;
                LoadError = "Disk-image file — content could not be read.";
            });
            return;
        }

        try
        {
            using var ms = new MemoryStream(payload);
            var frame = ImageDecoder.Decode(ms);

            if (token.IsCancellationRequested) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested) return;
                Source    = frame;
                LoadError = null;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode disk-image file: {Name}", row.DisplayName);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source    = null;
                LoadError = "Disk-image file — could not be decoded.";
            });
        }
    }

    // Loads a cached thumbnail for a ghost file, trying each cache source
    // in order: Thumbs.db → ZbThumbnail.info → Windows thumbcache.
    private void LoadGhostAsync(MediaEntityRow row, CancellationToken token)
    {
        byte[]? payload = null;

        if (!string.IsNullOrEmpty(row.ThumbsDbPath) && !string.IsNullOrEmpty(row.ThumbsDbStreamName))
        {
            try
            {
                var entry = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0, row.ThumbsDbStreamName, null, 0);
                payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
            }
            catch { /* try next source */ }
        }

        if (payload is null && !string.IsNullOrEmpty(row.ZbThumbnailPath) && row.ZbThumbnailDataSize > 0)
        {
            try
            {
                payload = ZbThumbnailReader.ExtractPayloadDirect(
                    row.ZbThumbnailPath, row.ZbThumbnailPayloadOffset, row.ZbThumbnailDataSize);
            }
            catch { /* try next source */ }
        }

        if (payload is null && !string.IsNullOrEmpty(row.ThumbcachePath) && row.ThumbcacheDataSize > 0)
        {
            try
            {
                payload = ThumbcacheReader.ExtractPayloadDirect(
                    row.ThumbcachePath, row.ThumbcachePayloadOffset, row.ThumbcacheDataSize);
            }
            catch { /* no more sources */ }
        }

        if (token.IsCancellationRequested) return;

        if (payload is null || payload.Length == 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source         = null;
                IsGhostPreview = false;
                LoadError      = "Ghost file \u2014 no cached thumbnail available.";
            });
            return;
        }

        try
        {
            using var ms    = new MemoryStream(payload);
            var       dec   = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var       frame = dec.Frames[0];
            frame.Freeze();

            if (token.IsCancellationRequested) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested) return;
                Source         = frame;
                IsGhostPreview = true;
                LoadError      = null;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode ghost thumbnail: {Name}", row.DisplayName);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Source         = null;
                IsGhostPreview = false;
                LoadError      = "Ghost file \u2014 thumbnail could not be decoded.";
            });
        }
    }

    private void SetFitMode()
    {
        IsFitMode   = true;
        ScalingMode = BitmapScalingMode.HighQuality;
    }

    private void SetActualSizeMode()
    {
        // NearestNeighbor gives exact pixel mapping at 1:1 — avoids hidden smoothing.
        IsFitMode   = false;
        ScalingMode = BitmapScalingMode.NearestNeighbor;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
