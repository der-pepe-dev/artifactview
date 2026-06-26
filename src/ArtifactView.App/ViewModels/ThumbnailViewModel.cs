using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ArtifactView.App.Viewing;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.ThumbCache;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App.ViewModels;

public sealed class ThumbnailViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger<ThumbnailViewModel> _logger;
    private BitmapSource? _source;
    private string _info = string.Empty;
    private BitmapSource? _cacheSource;
    private string _cacheInfo = string.Empty;
    private BitmapSource? _zbThumbnailSource;
    private string _zbThumbnailInfo = string.Empty;
    private BitmapSource? _mainPreviewSource;
    private string _thumbnailCompareResult = string.Empty;
    private CancellationTokenSource _loadCts = new();

    public ThumbnailViewModel(ILogger<ThumbnailViewModel> logger)
    {
        _logger = logger;
    }

    public BitmapSource? Source
    {
        get => _source;
        private set { _source = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => _source is not null;

    // Dimensions of the embedded thumbnail, or a status message when absent.
    public string Info
    {
        get => _info;
        private set { _info = value; OnPropertyChanged(); }
    }

    // ── Thumbs.db cached thumbnail ───────────────────────────────────────
    public BitmapSource? CacheSource
    {
        get => _cacheSource;
        private set { _cacheSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCacheThumbnail)); }
    }

    public bool HasCacheThumbnail => _cacheSource is not null;

    public string CacheInfo
    {
        get => _cacheInfo;
        private set { _cacheInfo = value; OnPropertyChanged(); }
    }

    // ── ZbThumbnail.info cached thumbnail ─────────────────────────────
    public BitmapSource? ZbThumbnailSource
    {
        get => _zbThumbnailSource;
        private set { _zbThumbnailSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasZbThumbnail)); }
    }

    public bool HasZbThumbnail => _zbThumbnailSource is not null;

    public string ZbThumbnailInfo
    {
        get => _zbThumbnailInfo;
        private set { _zbThumbnailInfo = value; OnPropertyChanged(); }
    }

    // ── Representative frame comparison (EXIF thumb vs. main image) ─────
    // Downsampled main image for visual side-by-side; null until loaded.
    public BitmapSource? MainPreviewSource
    {
        get => _mainPreviewSource;
        private set { _mainPreviewSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMainPreview)); }
    }

    public bool HasMainPreview => _mainPreviewSource is not null;

    // Human-readable perceptual similarity result, e.g. "Visually consistent (dHash Δ2)".
    public string ThumbnailCompareResult
    {
        get => _thumbnailCompareResult;
        private set { _thumbnailCompareResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCompareResult)); }
    }

    public bool HasCompareResult => !string.IsNullOrEmpty(_thumbnailCompareResult);

    // ── Embedded artifact images (depth maps, XMP base64, gain maps) ──
    public sealed record DecodedArtifactImage(BitmapSource Source, string Label, string Info);

    public ObservableCollection<DecodedArtifactImage> EmbeddedImages { get; } = [];
    public bool HasEmbeddedImages => EmbeddedImages.Count > 0;

    // Formats that never carry EXIF thumbnails — skip extraction attempt entirely.
    private static readonly HashSet<string> s_noThumbnailFormats =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".gif", ".bmp", ".webp", ".avif" };

    private static readonly HashSet<string> s_jpegExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    public void LoadAsync(MediaEntityRow? row)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        Source               = null;
        Info                 = string.Empty;
        CacheSource          = null;
        CacheInfo            = string.Empty;
        ZbThumbnailSource    = null;
        ZbThumbnailInfo      = string.Empty;
        MainPreviewSource    = null;
        ThumbnailCompareResult = string.Empty;
        EmbeddedImages.Clear();
        OnPropertyChanged(nameof(HasEmbeddedImages));

        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath))
            return;

        var path  = row.LogicalPath;
        var ext   = Path.GetExtension(path);
        var token = _loadCts.Token;

        // ── Thumbs.db cached thumbnail (runs for both live and ghost rows) ──
        if (!string.IsNullOrEmpty(row.ThumbsDbPath) &&
            !string.IsNullOrEmpty(row.ThumbsDbStreamName))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var entry   = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0,
                        row.ThumbsDbStreamName, null, 0);
                    var payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
                    if (token.IsCancellationRequested || payload is null)
                        return;

                    using var ms = new MemoryStream(payload);
                    var dec   = BitmapDecoder.Create(ms,
                        BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    var frame = dec.Frames[0];
                    frame.Freeze();

                    if (token.IsCancellationRequested) return;

                    var dateHint = row.ThumbsDbModifiedUtc.HasValue
                        ? $", cached {row.ThumbsDbModifiedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                        : string.Empty;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CacheSource = frame;
                        CacheInfo   = $"{frame.PixelWidth}\u00d7{frame.PixelHeight} px Thumbs.db thumbnail{dateHint}";
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Thumbs.db thumbnail extraction failed: {Path}", row.ThumbsDbPath);
                }
            }, token);
        }
        // ── Windows thumbcache fallback (when no Thumbs.db match) ────────
        else if (!string.IsNullOrEmpty(row.ThumbcachePath) && row.ThumbcacheDataSize > 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var payload = ThumbcacheReader.ExtractPayloadDirect(
                        row.ThumbcachePath, row.ThumbcachePayloadOffset, row.ThumbcacheDataSize);
                    if (token.IsCancellationRequested || payload is null || payload.Length == 0)
                        return;

                    using var ms = new MemoryStream(payload);
                    var dec   = BitmapDecoder.Create(ms,
                        BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    var frame = dec.Frames[0];
                    frame.Freeze();

                    if (token.IsCancellationRequested) return;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CacheSource = frame;
                        CacheInfo   = $"{frame.PixelWidth}\u00d7{frame.PixelHeight} px thumbcache thumbnail";
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Thumbcache extraction failed: {Path}", row.ThumbcachePath);
                }
            }, token);
        }

        // ── ZbThumbnail.info cached thumbnail ─────────────────────────────
        if (!string.IsNullOrEmpty(row.ZbThumbnailPath) && row.ZbThumbnailDataSize > 0)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var payload = ZbThumbnailReader.ExtractPayloadDirect(
                        row.ZbThumbnailPath, row.ZbThumbnailPayloadOffset, row.ZbThumbnailDataSize);
                    if (token.IsCancellationRequested || payload is null || payload.Length == 0)
                        return;

                    using var ms = new MemoryStream(payload);
                    var dec   = BitmapDecoder.Create(ms,
                        BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    var frame = dec.Frames[0];
                    frame.Freeze();

                    if (token.IsCancellationRequested) return;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        ZbThumbnailSource = frame;
                        ZbThumbnailInfo   = $"{frame.PixelWidth}\u00d7{frame.PixelHeight} px ZbThumbnail.info thumbnail";
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ZbThumbnail.info extraction failed: {Path}", row.ZbThumbnailPath);
                }
            }, token);
        }

        if (!File.Exists(path))
            return;

        // Skip the file-open entirely for formats that never carry EXIF thumbnails.
        if (s_noThumbnailFormats.Contains(ext))
        {
            Info = "No embedded thumbnail (format does not support EXIF thumbnails).";
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                // Use DecodeWithThumbnail (BitmapCacheOption.OnLoad) so the EXIF
                // APP1 is fully parsed. EmbeddedThumbnailDecoder.Extract uses None
                // which frequently misses the thumbnail at the decoder level.
                var (_, thumb) = ImageDecoder.DecodeWithThumbnail(path);
                if (token.IsCancellationRequested)
                    return;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    Source = thumb;
                    Info   = thumb is not null
                        ? $"{thumb.PixelWidth}\u00d7{thumb.PixelHeight} px embedded thumbnail"
                        : "No embedded thumbnail";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Thumbnail extraction failed: {Path}", path);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    Info = "Thumbnail not available");
            }
        }, token);

        // \u2500\u2500 Representative frame comparison: EXIF thumbnail vs. main image \u2500
        // Runs only for JPEG files that exist on disk.  Skips ghost files.
        if (s_jpegExtensions.Contains(ext) && File.Exists(path))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;

                    var thumbBytes = ImageMetadataExtractor.ExtractThumbnailBytes(path);
                    if (token.IsCancellationRequested || thumbBytes is null || thumbBytes.Length == 0)
                        return;

                    var finding = RepresentativeFrameAnalyzer.Analyze(path, thumbBytes);
                    if (token.IsCancellationRequested || finding is null) return;

                    // Load a small downsampled preview of the main image for side-by-side display.
                    BitmapSource? mainPreview = null;
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource        = new Uri(path, UriKind.Absolute);
                        bi.DecodePixelWidth = 200;
                        bi.CacheOption      = BitmapCacheOption.OnLoad;
                        bi.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                        bi.EndInit();
                        bi.Freeze();
                        mainPreview = bi;
                    }
                    catch { /* best-effort preview */ }

                    var resultText = finding.ReviewPriority switch
                    {
                        ReviewPriority.None   => $"Visually consistent \u2014 {finding.Observation}",
                        ReviewPriority.Low    => $"Minor differences \u2014 {finding.Observation}",
                        ReviewPriority.Medium => $"Significant differences \u2014 {finding.Observation}",
                        _                     => $"Content differs \u2014 {finding.Observation}"
                    };

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        ThumbnailCompareResult = resultText;
                        if (mainPreview is not null)
                            MainPreviewSource = mainPreview;
                    });
                }
                catch { /* best-effort */ }
            }, token);
        }

        // ── Embedded artifact images (depth maps, XMP base64, gain maps) ─
        // Scan and decode extractable image artifacts from the JPEG container
        // so they appear as rendered previews in the Artifacts tab.
        // Items are collected first and added in a single batch to avoid
        // modifying the ObservableCollection while WPF is laying out the tab.
        if (s_jpegExtensions.Contains(ext))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
                    var decoded = new List<DecodedArtifactImage>();

                    foreach (var artifact in artifacts)
                    {
                        if (token.IsCancellationRequested) return;
                        if (!artifact.IsExtractable) continue;
                        if (artifact.Type == EmbeddedArtifactType.MotionPhotoVideo) continue;

                        var payload = JpegEmbeddedArtifactScanner.ExtractPayload(path, artifact);
                        if (payload is null || payload.Length < 4) continue;
                        if (payload[0] != 0xFF && payload[0] != 0x89) continue;

                        using var ms = new MemoryStream(payload);
                        var dec = BitmapDecoder.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnLoad);
                        if (dec.Frames.Count == 0) continue;
                        var frame = dec.Frames[0];
                        frame.Freeze();

                        if (token.IsCancellationRequested) return;

                        var info = $"{frame.PixelWidth}\u00d7{frame.PixelHeight} px" +
                            (artifact.MimeType is not null ? $", {artifact.MimeType}" : "") +
                            $", confidence: {artifact.ParseConfidence.Label}";

                        decoded.Add(new(frame, artifact.DisplayName, info));
                    }

                    if (token.IsCancellationRequested || decoded.Count == 0) return;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        foreach (var item in decoded)
                            EmbeddedImages.Add(item);
                        OnPropertyChanged(nameof(HasEmbeddedImages));
                    });
                }
                catch { /* best-effort */ }
            }, token);
        }
    }

    public void Dispose()
    {
        _loadCts.Cancel();
        _loadCts.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
