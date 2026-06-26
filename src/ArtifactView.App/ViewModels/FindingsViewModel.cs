using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ArtifactView.App.Viewing;
using ArtifactView.Application.ViewModels;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.ThumbCache;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App.ViewModels;

public sealed class FindingsViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly HashSet<string> s_jpegExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    private static readonly HashSet<string> s_pngExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png" };

    private readonly ImageMetadataExtractor _extractor;
    private readonly ILogger<FindingsViewModel> _logger;
    private bool _isLoading;
    private CancellationTokenSource _loadCts = new();

    public FindingsViewModel(ImageMetadataExtractor extractor, ILogger<FindingsViewModel> logger)
    {
        _extractor = extractor;
        _logger    = logger;
    }

    public ObservableCollection<FindingRowViewModel> Entries { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFindings)); }
    }

    public bool HasFindings => Entries.Count > 0;

    // Highest ReviewPriority across all loaded findings.
    public ReviewPriority AggregateReviewPriority =>
        Entries.Count == 0
            ? ReviewPriority.None
            : Entries.Max(e => e.Finding.ReviewPriority);

    // Human-readable summary: counts by severity level (Medium+), or "All clear".
    public string AggregateText
    {
        get
        {
            if (Entries.Count == 0) return string.Empty;

            var critical = Entries.Count(e => e.Finding.ReviewPriority == ReviewPriority.Critical);
            var high     = Entries.Count(e => e.Finding.ReviewPriority == ReviewPriority.High);
            var medium   = Entries.Count(e => e.Finding.ReviewPriority == ReviewPriority.Medium);

            if (critical == 0 && high == 0 && medium == 0)
                return "All clear";

            var parts = new List<string>(3);
            if (critical > 0) parts.Add($"{critical} critical");
            if (high     > 0) parts.Add($"{high} high");
            if (medium   > 0) parts.Add($"{medium} medium");
            return string.Join(" · ", parts);
        }
    }

    // CSS-like severity label for the aggregate bar color: "critical", "high", "medium", "none".
    public string AggregateSeverityLevel => AggregateReviewPriority switch
    {
        ReviewPriority.Critical => "critical",
        ReviewPriority.High     => "high",
        ReviewPriority.Medium   => "medium",
        _                       => "none"
    };

    private void NotifyAggregateChanged()
    {
        OnPropertyChanged(nameof(AggregateReviewPriority));
        OnPropertyChanged(nameof(AggregateText));
        OnPropertyChanged(nameof(AggregateSeverityLevel));
        OnPropertyChanged(nameof(HasFindings));
    }

    // Called on the UI thread from ShellViewModel.SelectedItem.set.
    // When background enrichment has already populated row.CachedFindings,
    // those are shown immediately and only pixel-level comparisons
    // (thumbnail vs main, Thumbs.db visual) are added on top.
    public void LoadAsync(MediaEntityRow? row)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        Entries.Clear();
        NotifyAggregateChanged();

        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath))
        {
            IsLoading = false;
            return;
        }

        // ── Fast path: background enrichment already computed findings. ──
        // Show those immediately, then supplement with pixel-level checks.
        var cached = row.CachedFindings;
        if (cached is { Count: > 0 })
        {
            foreach (var f in cached)
                Entries.Add(new FindingRowViewModel(f));
            NotifyAggregateChanged();

            // Pixel-level comparisons were not run during background enrichment
            // because they require image decoding.  Run them now if the file is
            // a JPEG and still exists.
            var path  = row.LogicalPath;
            var ext   = Path.GetExtension(path);
            var token = _loadCts.Token;
            IsLoading = true;

            _ = Task.Run(() =>
            {
                var extras = new List<Finding>();
                try
                {
                    if (File.Exists(path) && s_jpegExtensions.Contains(ext))
                        RunPixelComparisons(path, ext, row, extras);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Pixel-level analysis incomplete for {Path}", path);
                }

                if (token.IsCancellationRequested) return;

                var warnCount = cached.Concat(extras).Count(f => f.ReviewPriority >= ReviewPriority.Medium);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    foreach (var f in extras)
                        Entries.Add(new FindingRowViewModel(f));
                    NotifyAggregateChanged();
                    row.FindingsText = warnCount > 0 ? $"{warnCount} \u26a0" : "\u2713";
                    IsLoading = false;
                });
            }, token);
            return;
        }

        // ── Slow path: no cache — run the full analysis. ────────────────
        if (!File.Exists(row.LogicalPath))
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        var slowPath  = row.LogicalPath;
        var slowExt   = Path.GetExtension(slowPath);
        var slowToken = _loadCts.Token;

        _ = Task.Run(() =>
        {
            var results = new List<Finding>();

            if (!File.Exists(slowPath))
            {
                results.Add(new Finding
                {
                    Id                      = "ghost-no-live-file",
                    Category                = "Provenance",
                    ReviewPriority          = ReviewPriority.Medium,
                    Observation             = $"\"{row.DisplayName}\" is not present on disk — only a cached thumbnail is available.",
                    ObservationConfidence   = new ConfidenceScore(99),
                    Interpretation          = "The file may have been deleted, moved, or renamed after the thumbnail was captured by Windows Explorer.",
                    InterpretationConfidence = new ConfidenceScore(75)
                });
            }
            else
            {
                try
                {
                    // ── Integrity ────────────────────────────────────────────────────────────
                    if (s_jpegExtensions.Contains(slowExt))
                        results.AddRange(JpegIntegrityAnalyzer.Analyze(slowPath));
                    else if (s_pngExtensions.Contains(slowExt))
                        results.AddRange(PngIntegrityAnalyzer.Analyze(slowPath));

                    // ── Format detection (magic bytes vs extension) ──────────────────
                    var formatFinding = FormatMismatchAnalyzer.Analyze(slowPath);
                    if (formatFinding is not null)
                        results.Add(formatFinding);

                    // ── Embedded artifacts (depth maps, gain maps, motion video, trailing data) ──
                    if (s_jpegExtensions.Contains(slowExt))
                        EmbeddedArtifactFindingsBuilder.AddFindings(
                            JpegEmbeddedArtifactScanner.Scan(slowPath), results);

                    // ── Provenance hash (runs for all formats) ───────────────────────
                    results.Add(FileHashAnalyzer.Analyze(slowPath));

                    // ── Thumbnail vs main, software detection, timestamp check ──
                    try
                    {
                        var (_, summary) = _extractor.Extract(slowPath);

                        BitmapSource? thumb     = null;
                        BitmapSource? mainImage = null;
                        if (s_jpegExtensions.Contains(slowExt))
                        {
                            var (frame, rawThumb) = ImageDecoder.DecodeWithThumbnail(slowPath);
                            mainImage = frame;
                            thumb     = rawThumb;
                        }

                        if (summary.HasThumbnail && summary.Width.HasValue && summary.Height.HasValue
                            && summary.ThumbnailHeight > 0 && summary.Height.Value > 0)
                        {
                            var thumbRatio = (double)summary.ThumbnailWidth!.Value / summary.ThumbnailHeight!.Value;
                            var mainRatio  = (double)summary.Width.Value / summary.Height.Value;
                            var ratioDiff  = Math.Abs(thumbRatio - mainRatio) / mainRatio;

                            if (ratioDiff > 0.02)
                            {
                                results.Add(new Finding
                                {
                                    Id = "thumb-aspect-ratio-mismatch",
                                    Category = "Thumbnail",
                                    ReviewPriority = ReviewPriority.Medium,
                                    Observation =
                                        $"Thumbnail aspect ratio ({summary.ThumbnailWidth}×{summary.ThumbnailHeight} → " +
                                        $"{thumbRatio:F3}) differs from main image ({summary.Width}×{summary.Height} " +
                                        $"→ {mainRatio:F3}).",
                                    ObservationConfidence = new ConfidenceScore(99),
                                    Interpretation =
                                        "Consistent with cropping or resizing after the original " +
                                        "thumbnail was written.",
                                    InterpretationConfidence = new ConfidenceScore(75)
                                });
                            }
                            else
                            {
                                results.Add(new Finding
                                {
                                    Id = "thumb-aspect-ratio-match",
                                    Category = "Thumbnail",
                                    ReviewPriority = ReviewPriority.None,
                                    Observation =
                                        $"Thumbnail aspect ratio ({summary.ThumbnailWidth}×{summary.ThumbnailHeight} → " +
                                        $"{thumbRatio:F3}) matches main image ({summary.Width}×{summary.Height} " +
                                        $"→ {mainRatio:F3}).",
                                    ObservationConfidence = new ConfidenceScore(95)
                                });

                                if (mainImage is not null && thumb is not null)
                                {
                                    var pixelFinding = ThumbnailContentAnalyzer.Analyze(thumb, mainImage);
                                    if (pixelFinding is not null)
                                        results.Add(pixelFinding);
                                }
                            }
                        }
                        else if (summary.Width.HasValue && !summary.HasThumbnail && thumb is null)
                        {
                            results.Add(new Finding
                            {
                                Id = "thumb-absent",
                                Category = "Thumbnail",
                                ReviewPriority = ReviewPriority.None,
                                Observation = "No embedded EXIF thumbnail found.",
                                ObservationConfidence = new ConfidenceScore(99)
                            });
                        }

                        if (summary.SoftwareTag is not null)
                            results.AddRange(SoftwareAnalyzer.Analyze(summary.SoftwareTag));

                        if (summary.CaptureDate.HasValue)
                            results.AddRange(TimestampConsistencyAnalyzer.Analyze(
                                slowPath,
                                summary.CaptureDate,
                                summary.DateTimeDigitized,
                                summary.DateTimeModified));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Metadata/thumbnail analysis incomplete for {Path}", slowPath);
                        results.Add(new Finding
                        {
                            Id = "meta-extraction-failed",
                            Category = "Metadata",
                            ReviewPriority = ReviewPriority.Low,
                            Observation = "Metadata extraction failed — software, timestamp, and thumbnail checks could not run.",
                            ObservationConfidence = new ConfidenceScore(99),
                            Interpretation = ex.Message,
                            InterpretationConfidence = new ConfidenceScore(90)
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Findings analysis failed for {Path}", slowPath);
                }

                // ── Thumbs.db cross-check (runs for live files with a cache match) ──
                if (File.Exists(slowPath) &&
                    !string.IsNullOrEmpty(row.ThumbsDbPath) &&
                    !string.IsNullOrEmpty(row.ThumbsDbStreamName))
                {
                    try
                    {
                        if (row.ThumbsDbModifiedUtc.HasValue)
                        {
                            var currentWrite = File.GetLastWriteTimeUtc(slowPath);
                            var cacheDelta   = currentWrite - row.ThumbsDbModifiedUtc.Value;

                            if (Math.Abs(cacheDelta.TotalSeconds) < 2)
                            {
                                results.Add(new Finding
                                {
                                    Id                       = "thumbsdb-date-match",
                                    Category                 = "Thumbs.db",
                                    ReviewPriority           = ReviewPriority.None,
                                    Observation              = $"File last-write ({currentWrite:yyyy-MM-dd HH:mm:ss} UTC) matches Thumbs.db cached date.",
                                    ObservationConfidence    = new ConfidenceScore(95)
                                });
                            }
                            else
                            {
                                var direction = cacheDelta.TotalSeconds > 0 ? "newer" : "older";
                                results.Add(new Finding
                                {
                                    Id                       = "thumbsdb-date-mismatch",
                                    Category                 = "Thumbs.db",
                                    ReviewPriority           = ReviewPriority.Medium,
                                    Observation              = $"File is {direction} than the Thumbs.db cached date by {cacheDelta.Duration():d\\.hh\\:mm\\:ss}.",
                                    ObservationConfidence    = new ConfidenceScore(90),
                                    Interpretation           = cacheDelta.TotalSeconds > 0
                                        ? "Consistent with the file being modified after the thumbnail was cached."
                                        : "File timestamp is earlier than cache — possible copy from another source or clock discrepancy.",
                                    InterpretationConfidence = new ConfidenceScore(70)
                                });
                            }
                        }

                        RunThumbsDbVisualComparison(slowPath, slowExt, row, results);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Thumbs.db cross-check skipped for {Path}", slowPath);
                    }
                }
            }

            if (slowToken.IsCancellationRequested)
                return;

            var warnCount = results.Count(f => f.ReviewPriority >= ReviewPriority.Medium);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (slowToken.IsCancellationRequested)
                    return;

                foreach (var f in results)
                    Entries.Add(new FindingRowViewModel(f));
                NotifyAggregateChanged();

                row.FindingsText = warnCount > 0 ? $"{warnCount} ⚠" : "✓";
                IsLoading = false;
            });
        }, slowToken);
    }

    // Pixel-level comparisons deferred from background enrichment.
    // Runs thumbnail vs main MAD and Thumbs.db visual comparison.
    private void RunPixelComparisons(string path, string ext, MediaEntityRow row, List<Finding> results)
    {
        if (!s_jpegExtensions.Contains(ext)) return;

        try
        {
            var (mainImage, thumb) = ImageDecoder.DecodeWithThumbnail(path);

            // Thumbnail vs main MAD — only when the cached findings already
            // include an aspect-ratio-match finding (ratios are close enough
            // for the pixel comparison to be meaningful).
            if (thumb is not null && mainImage is not null &&
                row.CachedFindings?.Exists(f => f.Id == "thumb-aspect-ratio-match") == true)
            {
                var pixelFinding = ThumbnailContentAnalyzer.Analyze(thumb, mainImage);
                if (pixelFinding is not null)
                    results.Add(pixelFinding);
            }

            // Thumbs.db visual comparison
            RunThumbsDbVisualComparison(path, ext, row, results);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pixel comparison failed for {Path}", path);
        }
    }

    private static void RunThumbsDbVisualComparison(string path, string ext, MediaEntityRow row, List<Finding> results)
    {
        if (string.IsNullOrEmpty(row.ThumbsDbPath) || string.IsNullOrEmpty(row.ThumbsDbStreamName))
            return;
        if (!s_jpegExtensions.Contains(ext))
            return;

        var entry   = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0,
            row.ThumbsDbStreamName, null, 0);
        var payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
        if (payload is null) return;

        BitmapSource? cacheThumb = null;
        try
        {
            using var ms = new MemoryStream(payload);
            var dec = BitmapDecoder.Create(ms,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            cacheThumb = dec.Frames[0];
            cacheThumb.Freeze();
        }
        catch { return; }

        var (_, rawThumb) = ImageDecoder.DecodeWithThumbnail(path);
        if (rawThumb is null) return;

        var pixelFinding = ThumbnailContentAnalyzer.Analyze(cacheThumb, rawThumb);
        if (pixelFinding is not null)
        {
            results.Add(new Finding
            {
                Id                       = "thumbsdb-vs-exif-" + pixelFinding.Id,
                Category                 = "Thumbs.db",
                ReviewPriority           = pixelFinding.ReviewPriority,
                Observation              = pixelFinding.Observation,
                ObservationConfidence    = pixelFinding.ObservationConfidence,
                Interpretation           = pixelFinding.Interpretation,
                InterpretationConfidence = pixelFinding.InterpretationConfidence
            });
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
