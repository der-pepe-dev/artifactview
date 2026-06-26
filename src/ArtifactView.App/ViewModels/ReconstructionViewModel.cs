using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ArtifactView.App.Commands;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Cache;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Reports;
using ArtifactView.Infrastructure.ThumbCache;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ArtifactView.App.ViewModels;

// Exact artifact extraction — saves embedded or cached image data to disk
// without modification.  Reconstructed outputs are clearly named so they
// are never mistaken for an original source file.
//
// Only exact binary extraction preserves the original lossy format.
// No metadata is injected or altered in the exported file.
public sealed class ReconstructionViewModel : INotifyPropertyChanged
{
    private readonly ImageMetadataExtractor _metadataExtractor;
    private readonly BlobStore? _blobStore;
    private readonly ILogger<ReconstructionViewModel> _logger;
    private MediaEntityRow? _currentRow;
    private bool _hasExifThumbnail;
    private bool _hasThumbsDbThumbnail;
    private bool _hasThumbcacheThumbnail;
    private bool _hasZbThumbnail;
    private string _exifThumbnailInfo = string.Empty;
    private string _statusText = string.Empty;
    private CancellationTokenSource _loadCts = new();

    public ReconstructionViewModel(ImageMetadataExtractor metadataExtractor, BlobStore? blobStore, ILogger<ReconstructionViewModel> logger)
    {
        _metadataExtractor = metadataExtractor;
        _blobStore = blobStore;
        _logger = logger;
        SaveExifThumbnailCommand      = new RelayCommand(_ => SaveExifThumbnail(),      _ => _hasExifThumbnail);
        SaveThumbsDbThumbnailCommand  = new RelayCommand(_ => SaveThumbsDbThumbnail(),  _ => _hasThumbsDbThumbnail);
        SaveThumbcacheThumbnailCommand = new RelayCommand(_ => SaveThumbcacheThumbnail(), _ => _hasThumbcacheThumbnail);
        SaveZbThumbnailCommand        = new RelayCommand(_ => SaveZbThumbnail(),        _ => _hasZbThumbnail);
        SaveEmbeddedArtifactCommand  = new RelayCommand(
            param => { if (param is EmbeddedArtifactRowViewModel vm) SaveEmbeddedArtifact(vm.Artifact); },
            param => param is EmbeddedArtifactRowViewModel { IsExtractable: true });
    }

    public bool HasExifThumbnail
    {
        get => _hasExifThumbnail;
        private set { _hasExifThumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyArtifact)); }
    }

    public bool HasThumbsDbThumbnail
    {
        get => _hasThumbsDbThumbnail;
        private set { _hasThumbsDbThumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyArtifact)); }
    }

    public bool HasThumbcacheThumbnail
    {
        get => _hasThumbcacheThumbnail;
        private set { _hasThumbcacheThumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyArtifact)); }
    }

    public bool HasZbThumbnail
    {
        get => _hasZbThumbnail;
        private set { _hasZbThumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyArtifact)); }
    }

    public bool HasAnyArtifact => _hasExifThumbnail || _hasThumbsDbThumbnail || _hasThumbcacheThumbnail || _hasZbThumbnail || EmbeddedArtifacts.Count > 0;

    public bool HasSelection => _currentRow is not null && !_currentRow.IsDirectory;

    // Embedded artifacts discovered by the JPEG artifact scanner (depth maps, gain maps, etc.)
    public ObservableCollection<EmbeddedArtifactRowViewModel> EmbeddedArtifacts { get; } = [];
    public bool HasEmbeddedArtifacts => EmbeddedArtifacts.Count > 0;

    // Brief description shown below the EXIF thumbnail row (e.g. "160×120, 8.2 KB").
    public string ExifThumbnailInfo
    {
        get => _exifThumbnailInfo;
        private set { _exifThumbnailInfo = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    public ICommand SaveExifThumbnailCommand      { get; }
    public ICommand SaveThumbsDbThumbnailCommand  { get; }
    public ICommand SaveThumbcacheThumbnailCommand { get; }
    public ICommand SaveZbThumbnailCommand          { get; }
    public ICommand SaveEmbeddedArtifactCommand   { get; }

    public void LoadAsync(MediaEntityRow? row)
    {
        // Cancel any in-progress background scan.
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        // Reset state synchronously on UI thread.
        _currentRow            = row;
        HasExifThumbnail       = false;
        HasThumbsDbThumbnail   = false;
        HasThumbcacheThumbnail = false;
        HasZbThumbnail         = false;
        ExifThumbnailInfo      = string.Empty;
        StatusText             = string.Empty;
        EmbeddedArtifacts.Clear();

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasEmbeddedArtifacts));
        OnPropertyChanged(nameof(HasAnyArtifact));

        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath))
            return;

        // Cache-based presence flags need no file I/O — set immediately.
        HasThumbsDbThumbnail   = !string.IsNullOrEmpty(row.ThumbsDbPath)
                              && !string.IsNullOrEmpty(row.ThumbsDbStreamName);
        HasThumbcacheThumbnail = !string.IsNullOrEmpty(row.ThumbcachePath)
                              && row.ThumbcacheDataSize > 0;
        HasZbThumbnail         = !string.IsNullOrEmpty(row.ZbThumbnailPath)
                              && row.ZbThumbnailDataSize > 0;

        // File I/O (EXIF probe + artifact scan) on background thread.
        // Scan() can take 1-3 s for large JPEG files with extended XMP.
        var token = _loadCts.Token;
        var path  = row.LogicalPath;
        var ext   = Path.GetExtension(path);

        _ = Task.Run(() => ScanArtifactsBackground(path, ext, token), token);
    }

    private async Task ScanArtifactsBackground(string path, string ext, CancellationToken token)
    {
        if (!File.Exists(path)) return;

        var isJpeg = ext.Equals(".jpg",  StringComparison.OrdinalIgnoreCase)
                  || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        var isTiff = ext.Equals(".tif",  StringComparison.OrdinalIgnoreCase)
                  || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

        // ── EXIF thumbnail probe ─────────────────────────────────────────
        bool   hasExif  = false;
        string exifInfo = string.Empty;

        if (isJpeg || isTiff)
        {
            try
            {
                var (_, summary) = _metadataExtractor.Extract(path);
                hasExif = summary.HasThumbnail;
                if (summary.HasThumbnail)
                {
                    var dims = $"{summary.ThumbnailWidth}\u00d7{summary.ThumbnailHeight}";
                    var size = summary.ThumbnailByteCount is > 0
                        ? $", {summary.ThumbnailByteCount.Value / 1024.0:F1} KB"
                        : string.Empty;
                    exifInfo = $"{dims}{size}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "EXIF thumbnail probe failed for {Path}", path);
            }
        }

        if (token.IsCancellationRequested) return;

        // ── Embedded artifact scan ───────────────────────────────────────
        List<EmbeddedArtifactRowViewModel>? artifactRows = null;
        if (isJpeg)
        {
            try
            {
                var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
                artifactRows  = artifacts
                    .Where(a => a.IsExtractable)
                    .Select(a => new EmbeddedArtifactRowViewModel(a))
                    .ToList();
            }
            catch { /* best-effort */ }
        }

        if (token.IsCancellationRequested) return;

        // ── Post results to UI thread ────────────────────────────────────
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;

            HasExifThumbnail  = hasExif;
            ExifThumbnailInfo = exifInfo;

            if (artifactRows is { Count: > 0 })
            {
                foreach (var vm in artifactRows)
                    EmbeddedArtifacts.Add(vm);

                OnPropertyChanged(nameof(HasEmbeddedArtifacts));
                OnPropertyChanged(nameof(HasAnyArtifact));

                foreach (var vm in artifactRows)
                    _ = vm.LoadPreviewAsync(a => JpegEmbeddedArtifactScanner.ExtractPayload(path, a));
            }
        }, System.Windows.Threading.DispatcherPriority.Background, token);
    }


    // ── EXIF thumbnail bit-copy extraction ─────────────────────────────
    //
    // Extracts the raw JPEG bytes stored in EXIF IFD1 and writes them to
    // disk unmodified.  No decode → re-encode step, so the output is a
    // bit-exact copy of the embedded thumbnail.

    private void SaveExifThumbnail()
    {
        var row = _currentRow;
        if (row is null || string.IsNullOrEmpty(row.LogicalPath))
            return;

        // Check BlobStore cache before re-reading the file.
        var cacheKey = $"{row.LogicalPath}:{File.GetLastWriteTimeUtc(row.LogicalPath):O}";
        byte[]? thumbBytes = null;
        if (_blobStore is not null && _blobStore.Exists("exif-thumbnail", cacheKey))
        {
            using var cacheStream = _blobStore.TryOpenReadAsync("exif-thumbnail", cacheKey).GetAwaiter().GetResult();
            if (cacheStream is not null)
            {
                using var ms = new MemoryStream();
                cacheStream.CopyTo(ms);
                thumbBytes = ms.ToArray();
            }
        }
        thumbBytes ??= ImageMetadataExtractor.ExtractThumbnailBytes(row.LogicalPath);
        if (thumbBytes is null || thumbBytes.Length == 0)
        {
            StatusText = "No embedded EXIF thumbnail found in this file.";
            return;
        }
        if (_blobStore is not null && !_blobStore.Exists("exif-thumbnail", cacheKey))
        {
            using var ms = new MemoryStream(thumbBytes);
            _blobStore.WriteAsync("exif-thumbnail", cacheKey, ms, CancellationToken.None)
                      .GetAwaiter().GetResult();
        }

        // Detect the payload format to choose the correct extension.
        var isJpeg   = thumbBytes.Length >= 3 && thumbBytes[0] == 0xFF && thumbBytes[1] == 0xD8 && thumbBytes[2] == 0xFF;
        var ext      = isJpeg ? ".jpg" : ".bin";
        var filter   = isJpeg ? "JPEG (original format)|*.jpg" : "Binary payload|*.bin";
        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);

        var dialog = new SaveFileDialog
        {
            Title    = "Save exact EXIF thumbnail (bit-copy)",
            FileName = $"{baseName}_exif_thumbnail{ext}",
            Filter   = filter
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, thumbBytes);
            ProvenanceSidecarWriter.Write(
                dialog.FileName,
                sourceFile:             row.LogicalPath,
                sourcePresence:         row.PresenceState.ToLowerInvariant(),
                extractionSource:       "exif-thumbnail",
                extractionMethod:       "bit-copy",
                reconstructionCategory: "exact-artifact-extraction",
                outputFormat:           isJpeg ? "image/jpeg" : "application/octet-stream",
                byteCount:              thumbBytes.Length,
                notes: "Exact binary extraction of EXIF IFD1 thumbnail. No decode or re-encode.");
            StatusText = $"EXIF thumbnail saved ({thumbBytes.Length:N0} bytes): {dialog.FileName}";
            _logger.LogInformation("Bit-copy EXIF thumbnail extraction: {Source} → {Dest} ({Bytes} bytes)",
                row.LogicalPath, dialog.FileName, thumbBytes.Length);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            _logger.LogError(ex, "EXIF thumbnail save failed for {Path}", row.LogicalPath);
        }
    }

    // ── Thumbs.db thumbnail exact extraction ─────────────────────────────

    private void SaveThumbsDbThumbnail()
    {
        var row = _currentRow;
        if (row is null || string.IsNullOrEmpty(row.ThumbsDbPath) ||
            string.IsNullOrEmpty(row.ThumbsDbStreamName))
            return;

        var entry   = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0,
            row.ThumbsDbStreamName, null, 0);
        var payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
        if (payload is null || payload.Length == 0)
        {
            StatusText = "Could not extract Thumbs.db cached thumbnail.";
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);

        // Thumbs.db payloads are usually JPEG.  Detect and preserve the
        // original format for exact binary extraction.
        var isJpeg = payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF;
        var ext    = isJpeg ? ".jpg" : ".bin";
        var filter = isJpeg ? "JPEG (original format)|*.jpg" : "Binary payload|*.bin";

        var dialog = new SaveFileDialog
        {
            Title    = "Save exact Thumbs.db cached thumbnail",
            FileName = $"{baseName}_thumbsdb_cache{ext}",
            Filter   = filter
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, payload);
            ProvenanceSidecarWriter.Write(
                dialog.FileName,
                sourceFile:             row.LogicalPath,
                sourcePresence:         row.PresenceState.ToLowerInvariant(),
                extractionSource:       "thumbs-db",
                extractionMethod:       "bit-copy",
                reconstructionCategory: "exact-artifact-extraction",
                outputFormat:           isJpeg ? "image/jpeg" : "application/octet-stream",
                byteCount:              payload.Length,
                contributors:           [$"Thumbs.db: {row.ThumbsDbPath} (stream: {row.ThumbsDbStreamName})"],
                notes: "Exact binary extraction from Thumbs.db compound document.");
            StatusText = $"Thumbs.db thumbnail saved: {dialog.FileName}";
            _logger.LogInformation("Exact Thumbs.db extraction: {DbPath}/{Stream} → {Dest}",
                row.ThumbsDbPath, row.ThumbsDbStreamName, dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            _logger.LogError(ex, "Thumbs.db thumbnail save failed for {Stream}", row.ThumbsDbStreamName);
        }
    }

    // ── Windows thumbcache thumbnail exact extraction ─────────────────────

    private void SaveThumbcacheThumbnail()
    {
        var row = _currentRow;
        if (row is null || string.IsNullOrEmpty(row.ThumbcachePath) || row.ThumbcacheDataSize <= 0)
            return;

        var payload = ThumbcacheReader.ExtractPayloadDirect(
            row.ThumbcachePath, row.ThumbcachePayloadOffset, row.ThumbcacheDataSize);
        if (payload is null || payload.Length == 0)
        {
            StatusText = "Could not extract thumbcache payload — the cache may have been cleared.";
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);
        var isJpeg   = payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF;
        var ext      = isJpeg ? ".jpg" : ".bin";
        var filter   = isJpeg ? "JPEG (original format)|*.jpg" : "Binary payload|*.bin";

        var dialog = new SaveFileDialog
        {
            Title    = "Save exact thumbcache thumbnail (bit-copy)",
            FileName = $"{baseName}_thumbcache{ext}",
            Filter   = filter
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, payload);
            ProvenanceSidecarWriter.Write(
                dialog.FileName,
                sourceFile:             row.LogicalPath,
                sourcePresence:         row.PresenceState.ToLowerInvariant(),
                extractionSource:       "thumbcache",
                extractionMethod:       "bit-copy",
                reconstructionCategory: "exact-artifact-extraction",
                outputFormat:           isJpeg ? "image/jpeg" : "application/octet-stream",
                byteCount:              payload.Length,
                contributors:           [$"thumbcache: {row.ThumbcachePath} (hash: {row.ThumbcacheHash:X16})"],
                notes: "Exact binary extraction from Windows thumbcache_*.db.");
            StatusText = $"Thumbcache thumbnail saved ({payload.Length:N0} bytes): {dialog.FileName}";
            _logger.LogInformation("Thumbcache extraction: hash {Hash:X16} → {Dest} ({Bytes} bytes)",
                row.ThumbcacheHash, dialog.FileName, payload.Length);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            _logger.LogError(ex, "Thumbcache save failed for hash {Hash:X16}", row.ThumbcacheHash);
        }
    }

    // ── ZbThumbnail.info thumbnail exact extraction ───────────────────────

    private void SaveZbThumbnail()
    {
        var row = _currentRow;
        if (row is null || string.IsNullOrEmpty(row.ZbThumbnailPath) || row.ZbThumbnailDataSize <= 0)
            return;

        var payload = ZbThumbnailReader.ExtractPayloadDirect(
            row.ZbThumbnailPath, row.ZbThumbnailPayloadOffset, row.ZbThumbnailDataSize);
        if (payload is null || payload.Length == 0)
        {
            StatusText = "Could not extract ZbThumbnail.info payload.";
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);
        var isJpeg   = payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF;
        var ext      = isJpeg ? ".jpg" : ".bin";
        var filter   = isJpeg ? "JPEG (original format)|*.jpg" : "Binary payload|*.bin";

        var dialog = new SaveFileDialog
        {
            Title    = "Save exact ZbThumbnail.info cached thumbnail (bit-copy)",
            FileName = $"{baseName}_zbthumbnail{ext}",
            Filter   = filter
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, payload);
            ProvenanceSidecarWriter.Write(
                dialog.FileName,
                sourceFile:             row.LogicalPath,
                sourcePresence:         row.PresenceState.ToLowerInvariant(),
                extractionSource:       "zb-thumbnail",
                extractionMethod:       "bit-copy",
                reconstructionCategory: "exact-artifact-extraction",
                outputFormat:           isJpeg ? "image/jpeg" : "application/octet-stream",
                byteCount:              payload.Length,
                contributors:           [$"ZbThumbnail.info: {row.ZbThumbnailPath}"],
                notes: "Exact binary extraction from Zoner Photo Studio ZbThumbnail.info cache.");
            StatusText = $"ZbThumbnail.info thumbnail saved ({payload.Length:N0} bytes): {dialog.FileName}";
            _logger.LogInformation("ZbThumbnail.info extraction: {Source} → {Dest} ({Bytes} bytes)",
                row.ZbThumbnailPath, dialog.FileName, payload.Length);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            _logger.LogError(ex, "ZbThumbnail.info save failed for {Path}", row.ZbThumbnailPath);
        }
    }

    // ── Embedded artifact extraction

    private void SaveEmbeddedArtifact(EmbeddedArtifact artifact)
    {
        var row = _currentRow;
        if (row is null || string.IsNullOrEmpty(row.LogicalPath))
            return;

        var payload = JpegEmbeddedArtifactScanner.ExtractPayload(row.LogicalPath, artifact);
        if (payload is null || payload.Length == 0)
        {
            StatusText = $"Could not extract {artifact.DisplayName}.";
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);
        var suffix   = artifact.Type switch
        {
            EmbeddedArtifactType.DepthMap         => "_depth_map",
            EmbeddedArtifactType.GainMap          => "_gain_map",
            EmbeddedArtifactType.MotionPhotoVideo => "_motion",
            EmbeddedArtifactType.SecondaryImage   => "_secondary",
            _                                     => "_embedded"
        };
        var ext = artifact.MimeType switch
        {
            "video/mp4" => ".mp4",
            "image/jpeg" => ".jpg",
            "image/png"  => ".png",
            _            => ".bin"
        };
        var filter = ext switch
        {
            ".mp4" => "MP4 video|*.mp4",
            ".jpg" => "JPEG (original format)|*.jpg",
            ".png" => "PNG|*.png",
            _      => "Binary payload|*.bin"
        };

        var dialog = new SaveFileDialog
        {
            Title    = $"Save {artifact.DisplayName} (bit-copy)",
            FileName = $"{baseName}{suffix}{ext}",
            Filter   = filter
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, payload);
            var mimeType = artifact.MimeType ?? "application/octet-stream";
            ProvenanceSidecarWriter.Write(
                dialog.FileName,
                sourceFile:             row.LogicalPath,
                sourcePresence:         row.PresenceState.ToLowerInvariant(),
                extractionSource:       "embedded-artifact",
                extractionMethod:       "bit-copy",
                reconstructionCategory: "exact-artifact-extraction",
                outputFormat:           mimeType,
                byteCount:              payload.Length,
                contributors:           [$"Embedded in: {row.LogicalPath}", $"Artifact type: {artifact.Type}"],
                notes: $"Exact binary extraction of embedded {artifact.Type} from source file. Source namespace: {artifact.SourceNamespace ?? "unknown"}.");
            StatusText = $"{artifact.DisplayName} saved ({payload.Length:N0} bytes): {dialog.FileName}";
            _logger.LogInformation("Embedded artifact extraction: {Type} from {Source} → {Dest} ({Bytes} bytes)",
                artifact.Type, row.LogicalPath, dialog.FileName, payload.Length);
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            _logger.LogError(ex, "Embedded artifact save failed for {Type} in {Path}", artifact.Type, row.LogicalPath);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
