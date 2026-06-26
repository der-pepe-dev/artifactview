using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArtifactView.App.Viewing;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.App.Views;

public partial class FullscreenViewerWindow : Window
{
    private readonly List<MediaEntityRow> _rows;
    private int _currentIndex;

    // Exposes the index the user navigated to, so the caller can sync
    // the main grid selection after the fullscreen dialog closes.
    public int CurrentIndex => _currentIndex;
    private double _zoomLevel = 1.0;
    private bool _isFitMode = true;
    private CancellationTokenSource _loadCts = new();

    // Available image versions for the current file.  After all versions are
    // loaded they are sorted so that TAB (backward) cycling shows:
    //   Original → biggest artifact → … → smallest → depth map → Original.
    private enum VersionCategory { DepthMap = 0, Artifact = 1, Original = 2 }
    private readonly record struct ImageVersion(BitmapSource Source, string Label, VersionCategory Category = VersionCategory.Artifact);
    private readonly List<ImageVersion> _versions = [];
    private int _versionIndex;

    // Current display rotation in degrees (0, 90, 180, 270).
    // Persists across version cycling but resets on file navigation.
    private double _rotationDegrees;

    // Color inversion — persists across version cycling, resets on file navigation.
    private bool _isInverted;

    // Overlay auto-hides after 3 s of inactivity; any interaction resets the timer.
    private readonly DispatcherTimer _overlayTimer = new()
        { Interval = TimeSpan.FromSeconds(3) };

    public FullscreenViewerWindow(IReadOnlyList<MediaEntityRow> rows, int initialIndex,
                                  BitmapSource? preloaded = null)
    {
        InitializeComponent();

        _rows = [.. rows];
        _currentIndex = Math.Clamp(initialIndex, 0, Math.Max(0, rows.Count - 1));

        _overlayTimer.Tick += (_, _) => HideOverlay();
        MouseMove += (_, _) => { if (!_isDragging) ShowOverlay(); };

        if (preloaded is not null)
        {
            // Show the preloaded image immediately while the async load
            // populates _versions with all available quality tiers.
            FitImage.Source  = preloaded;
            ZoomImage.Source = preloaded;
        }

        // Always load asynchronously — this populates _versions for TAB cycling
        // and replaces the preloaded image with the full decode when ready.
        _ = LoadCurrentAsync();

        Loaded += (_, _) => { Focus(); ShowOverlay(); };
    }

    // ── Overlay ───────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _overlayTimer.Stop();
        _loadCts.Cancel();
        _loadCts.Dispose();
        base.OnClosed(e);
    }

    private void ShowOverlay()
    {
        OverlayBar.Visibility = Visibility.Visible;
        Cursor = Cursors.Arrow;
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    private void HideOverlay()
    {
        _overlayTimer.Stop();
        OverlayBar.Visibility = Visibility.Collapsed;
        Cursor = Cursors.None;
    }

    // ── Image loading ─────────────────────────────────────────────────────────

    private async Task LoadCurrentAsync()
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        var token = _loadCts.Token;
        var row   = _rows[_currentIndex];

        _versions.Clear();
        _versionIndex = -1;
        _rotationDegrees = 0;
        _isInverted = false;
        ApplyRotation();
        ApplyInvert();

        // Only blank the display if nothing is already shown (avoids flash
        // when a preloaded image from the main viewer is already visible).
        if (FitImage.Source is null)
        {
            LoadingText.Text       = "Loading\u2026";
            LoadingText.Visibility = Visibility.Visible;
        }
        UpdateOverlayText();
        ShowOverlay();

        try
        {
            // ── Thumbs.db cached thumbnail (lowest quality) ──────────────
            if (!string.IsNullOrEmpty(row.ThumbsDbPath) &&
                !string.IsNullOrEmpty(row.ThumbsDbStreamName))
            {
                var entry   = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0,
                    row.ThumbsDbStreamName, null, 0);
                var payload = await Task.Run(
                    () => ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry), token);

                if (token.IsCancellationRequested) return;

                if (payload is not null)
                {
                    var bmp = await Task.Run(() =>
                    {
                        using var ms = new MemoryStream(payload);
                        var dec = BitmapDecoder.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnLoad);
                        var frame = dec.Frames[0];
                        frame.Freeze();
                        return frame;
                    }, token);

                    if (token.IsCancellationRequested) return;
                    _versions.Add(new(bmp, "Thumbs.db cached thumbnail"));
                    ShowBestAvailable();
                }
            }
            // ── Windows thumbcache fallback ───────────────────────────────
            else if (!string.IsNullOrEmpty(row.ThumbcachePath) && row.ThumbcacheDataSize > 0)
            {
                var payload = await Task.Run(() =>
                    ThumbcacheReader.ExtractPayloadDirect(
                        row.ThumbcachePath, row.ThumbcachePayloadOffset, row.ThumbcacheDataSize),
                    token);

                if (token.IsCancellationRequested) return;

                if (payload is not null)
                {
                    var bmp = await Task.Run(() =>
                    {
                        using var ms = new MemoryStream(payload);
                        var dec = BitmapDecoder.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnLoad);
                        var frame = dec.Frames[0];
                        frame.Freeze();
                        return frame;
                    }, token);

                    if (token.IsCancellationRequested) return;
                    _versions.Add(new(bmp, "Thumbcache cached thumbnail"));
                    ShowBestAvailable();
                }
            }
            // ── ZbThumbnail.info cached thumbnail ─────────────────────────
            else if (!string.IsNullOrEmpty(row.ZbThumbnailPath) && row.ZbThumbnailDataSize > 0)
            {
                var payload = await Task.Run(() =>
                    ZbThumbnailReader.ExtractPayloadDirect(
                        row.ZbThumbnailPath, row.ZbThumbnailPayloadOffset, row.ZbThumbnailDataSize),
                    token);

                if (token.IsCancellationRequested) return;

                if (payload is not null)
                {
                    var bmp = await Task.Run(() =>
                    {
                        using var ms = new MemoryStream(payload);
                        var dec = BitmapDecoder.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnLoad);
                        var frame = dec.Frames[0];
                        frame.Freeze();
                        return frame;
                    }, token);

                    if (token.IsCancellationRequested) return;
                    _versions.Add(new(bmp, "ZbThumbnail.info cached thumbnail"));
                    ShowBestAvailable();
                }
            }

            // Ghost files have no live bytes — stop here.
            if (row.PresenceState == "Ghost")
            {
                if (_versions.Count == 0)
                    LoadingText.Text = "Ghost file \u2014 no cached thumbnail.";
                else
                    LoadingText.Visibility = Visibility.Collapsed;

                _versionIndex = _versions.Count - 1;
                UpdateOverlayText();
                return;
            }

            var path = row.LogicalPath;

            // ── EXIF thumbnail + full decode in a single pass ────────────
            // DecodeWithThumbnail uses BitmapCacheOption.OnLoad which properly
            // parses the EXIF APP1 segment.  EmbeddedThumbnailDecoder.Extract
            // uses None which frequently returns null even when a thumbnail
            // exists — that was the reason version cycling never had > 1 entry.
            var (full, exifThumb) = await Task.Run(
                () => ImageDecoder.DecodeWithThumbnail(path), token);
            if (token.IsCancellationRequested) return;

            if (exifThumb is not null)
            {
                _versions.Add(new(exifThumb, "Embedded EXIF thumbnail"));
                ShowBestAvailable();
            }

            _versions.Add(new(full, "Full decoded image", VersionCategory.Original));
            _versionIndex = _versions.Count - 1;

            FitImage.Source        = full;
            ZoomImage.Source       = full;
            LoadingText.Visibility = Visibility.Collapsed;
            UpdateScalingMode();
            UpdateOverlayText();

            // ── Embedded artifact images (depth maps, XMP images) ─────────
            // Decode all extractable image artifacts, then add them in one
            // batch and sort so Tab cycling follows the desired order:
            //   Original → biggest → … → smallest → depth map → Original.
            _ = Task.Run(() =>
            {
                try
                {
                    var artifacts = JpegEmbeddedArtifactScanner.Scan(path);
                    var decoded = new List<ImageVersion>();

                    foreach (var artifact in artifacts)
                    {
                        if (token.IsCancellationRequested) return;
                        if (!artifact.IsExtractable) continue;

                        // Skip non-image artifacts (e.g. motion video).
                        if (artifact.Type == EmbeddedArtifactType.MotionPhotoVideo) continue;

                        var payload = JpegEmbeddedArtifactScanner.ExtractPayload(path, artifact);
                        if (payload is null || payload.Length < 4) continue;

                        // Verify it's a decodable image.
                        if (payload[0] != 0xFF && payload[0] != 0x89) continue;

                        using var ms = new MemoryStream(payload);
                        var dec   = BitmapDecoder.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile,
                            BitmapCacheOption.OnLoad);
                        if (dec.Frames.Count == 0) continue;
                        var frame = dec.Frames[0];
                        frame.Freeze();

                        if (token.IsCancellationRequested) return;

                        var category = artifact.Type == EmbeddedArtifactType.DepthMap
                            ? VersionCategory.DepthMap
                            : VersionCategory.Artifact;
                        decoded.Add(new(frame, artifact.DisplayName, category));
                    }

                    if (token.IsCancellationRequested) return;

                    Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        foreach (var v in decoded)
                            _versions.Add(v);
                        SortVersions();
                        UpdateOverlayText();
                    });
                }
                catch { /* best-effort */ }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (_versions.Count == 0)
            {
                FitImage.Source  = null;
                ZoomImage.Source = null;
                LoadingText.Text       = "Could not decode image.";
                LoadingText.Visibility = Visibility.Visible;
            }
        }
    }

    // Shows the best version collected so far as a quick preview.
    private void ShowBestAvailable()
    {
        if (_versions.Count == 0) return;
        var best = _versions[^1].Source;
        FitImage.Source        = best;
        ZoomImage.Source       = best;
        _versionIndex          = _versions.Count - 1;
        LoadingText.Visibility = Visibility.Collapsed;
        UpdateOverlayText();
    }

    // Switches the display to the version at _versionIndex.
    private void ShowVersion()
    {
        if (_versionIndex < 0 || _versionIndex >= _versions.Count) return;
        var ver = _versions[_versionIndex];
        FitImage.Source  = ver.Source;
        ZoomImage.Source = ver.Source;
        ApplyRotation();
        UpdateScalingMode();
        UpdateOverlayText();
        ShowOverlay();
    }

    // Sorts _versions so that Tab (backward) cycling shows:
    //   Original → biggest artifact → … → smallest artifact → depth map → Original.
    // List order after sort: depth maps (by area), artifacts (by area), original.
    // Tab decrements from the end, so last = first seen.
    private void SortVersions()
    {
        _versions.Sort((a, b) =>
        {
            var cmp = a.Category.CompareTo(b.Category);
            if (cmp != 0) return cmp;
            var areaA = (long)a.Source.PixelWidth * a.Source.PixelHeight;
            var areaB = (long)b.Source.PixelWidth * b.Source.PixelHeight;
            return areaA.CompareTo(areaB);
        });
        _versionIndex = _versions.Count - 1;
    }

    // Applies the current rotation angle to both image elements.
    private void ApplyRotation()
    {
        FitRotation.Angle  = _rotationDegrees;
        ZoomRotation.Angle = _rotationDegrees;
    }

    private void Rotate(double delta)
    {
        _rotationDegrees = (_rotationDegrees + delta) % 360;
        if (_rotationDegrees < 0) _rotationDegrees += 360;
        ApplyRotation();
        UpdateOverlayText();
        ShowOverlay();
    }

    private void ToggleInvert()
    {
        _isInverted = !_isInverted;
        ApplyInvert();
        UpdateOverlayText();
        ShowOverlay();
    }

    private void ApplyInvert()
    {
        var effect = _isInverted ? new Effects.InvertColorEffect() : null;
        FitImage.Effect  = effect;
        ZoomImage.Effect = effect;
    }

    // ── Display state ─────────────────────────────────────────────────────────

    private void ApplyMode()
    {
        if (_isFitMode)
        {
            FitImage.Visibility   = Visibility.Visible;
            ZoomViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            FitImage.Visibility   = Visibility.Collapsed;
            ZoomViewer.Visibility = Visibility.Visible;
            ZoomTransform.ScaleX  = _zoomLevel;
            ZoomTransform.ScaleY  = _zoomLevel;
        }

        UpdateScalingMode();
        UpdateOverlayText();
        ShowOverlay();
    }

    private void UpdateScalingMode()
    {
        // Exact pixel mapping at or above 1:1; smooth scaling when zoomed out or in fit mode.
        var mode = (!_isFitMode && _zoomLevel >= 1.0)
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality;
        RenderOptions.SetBitmapScalingMode(FitImage,  mode);
        RenderOptions.SetBitmapScalingMode(ZoomImage, mode);
    }

    private void UpdateOverlayText()
    {
        if (_rows.Count == 0) return;
        var row    = _rows[_currentIndex];
        var prefix = row.PresenceState == "Ghost" ? "\U0001F47B " : string.Empty;

        var versionLabel = string.Empty;
        if (_versionIndex >= 0 && _versionIndex < _versions.Count)
        {
            var ver = _versions[_versionIndex];
            versionLabel = $"  \u2014  {ver.Label}  ({ver.Source.PixelWidth}\u00d7{ver.Source.PixelHeight})";
        }

        FileInfoText.Text = $"{prefix}{row.DisplayName}{versionLabel}   ({_currentIndex + 1} / {_rows.Count})";

        var rotLabel = _rotationDegrees is > 0 and < 360 ? $"  \u21bb{_rotationDegrees:F0}\u00b0" : "";
        var invLabel = _isInverted ? "  INV" : "";
        ZoomInfoText.Text = (_isFitMode ? "Fit" : $"{_zoomLevel * 100:F0}%") + rotLabel + invLabel;
    }

    private void EnterZoomMode(double newLevel)
    {
        _isFitMode = false;
        _zoomLevel = Math.Clamp(newLevel, 0.1, 8.0);
        ApplyMode();
    }

    // Computes the effective scale factor that Fit mode uses for the current image.
    // Returns 1.0 as fallback if the image or window dimensions are unavailable.
    private double GetFitScale()
    {
        var src = FitImage.Source as BitmapSource;
        if (src is null || ActualWidth <= 0 || ActualHeight <= 0)
            return 1.0;

        // At 90° or 270° the image is rendered transposed, so swap pixel dimensions
        // to match the orientation that Stretch="Uniform" is actually fitting to.
        var rotated  = _rotationDegrees is 90.0 or 270.0;
        var effectiveW = rotated ? src.PixelHeight : src.PixelWidth;
        var effectiveH = rotated ? src.PixelWidth  : src.PixelHeight;
        return Math.Min(ActualWidth / effectiveW, ActualHeight / effectiveH);
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        ShowOverlay();

        switch (e.Key)
        {
            case Key.Escape: Close(); break;

            case Key.Z:
            case Key.Space:
                if (_isFitMode) EnterZoomMode(1.0);
                else { _isFitMode = true; ApplyMode(); }
                break;

            case Key.OemPlus:
            case Key.Add:
                EnterZoomMode((_isFitMode ? GetFitScale() : _zoomLevel) * 1.25);
                break;

            case Key.OemMinus:
            case Key.Subtract:
                EnterZoomMode((_isFitMode ? GetFitScale() : _zoomLevel) / 1.25);
                break;

            case Key.Left:
            case Key.Up:
                if (_currentIndex > 0) { _currentIndex--; _ = LoadCurrentAsync(); }
                break;

            case Key.Right:
            case Key.Down:
            case Key.Return:
                if (_currentIndex < _rows.Count - 1) { _currentIndex++; _ = LoadCurrentAsync(); }
                break;

            case Key.Home:
                if (_currentIndex != 0) { _currentIndex = 0; _ = LoadCurrentAsync(); }
                break;

            case Key.End:
                if (_currentIndex != _rows.Count - 1) { _currentIndex = _rows.Count - 1; _ = LoadCurrentAsync(); }
                break;

            // Cycle image versions: original → biggest → … → smallest → depth map → original
            case Key.Tab:
                if (_versions.Count > 1)
                {
                    _versionIndex = (_versionIndex - 1 + _versions.Count) % _versions.Count;
                    ShowVersion();
                }
                break;

            // Rotate the display — does not modify source pixels.
            case Key.L: Rotate(-90); break;
            case Key.R: Rotate(90);  break;

            // Invert colors — forensic aid for viewing faint detail.
            case Key.I: ToggleInvert(); break;
        }

        e.Handled = true;
    }

    // ── Mouse wheel ── navigate previous/next file ───────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta > 0 && _currentIndex > 0)
        {
            _currentIndex--;
            _ = LoadCurrentAsync();
        }
        else if (e.Delta < 0 && _currentIndex < _rows.Count - 1)
        {
            _currentIndex++;
            _ = LoadCurrentAsync();
        }
        e.Handled = true;
    }

    // ── Double-click ── toggle fit / 1:1 ─────────────────────────────────────

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isFitMode)
            EnterZoomMode(1.0);
        else
            { _isFitMode = true; ApplyMode(); }

        e.Handled = true;
    }

    // ── Drag-to-pan in zoom mode ─────────────────────────────────────────────

    private Point _dragStart;
    private double _dragHOffset;
    private double _dragVOffset;
    private bool _isDragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_isFitMode || e.ClickCount > 1) return;

        _isDragging   = true;
        _dragStart    = e.GetPosition(ZoomViewer);
        _dragHOffset  = ZoomViewer.HorizontalOffset;
        _dragVOffset  = ZoomViewer.VerticalOffset;
        Cursor        = Cursors.ScrollAll;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging) return;

        var pos   = e.GetPosition(ZoomViewer);
        var dx    = pos.X - _dragStart.X;
        var dy    = pos.Y - _dragStart.Y;
        ZoomViewer.ScrollToHorizontalOffset(_dragHOffset - dx);
        ZoomViewer.ScrollToVerticalOffset(_dragVOffset - dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        ShowOverlay();
        e.Handled = true;
    }
}
