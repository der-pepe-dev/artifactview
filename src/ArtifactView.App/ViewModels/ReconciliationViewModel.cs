using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ArtifactView.Core.Models;
using ArtifactView.Core.Services;
using ArtifactView.Infrastructure.Metadata;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App.ViewModels;

// Reconciles metadata from multiple evidence sources (EXIF dates, filesystem
// timestamps, Thumbs.db dates) and surfaces conflicts.  Each reconciled field
// shows a preferred value, confidence, and any alternative values from other
// sources — making it clear when evidence agrees or disagrees.
public sealed class ReconciliationViewModel : INotifyPropertyChanged
{
    private readonly ImageMetadataExtractor _extractor;
    private readonly EvidenceReconciliationService _reconciler = new();
    private readonly ILogger<ReconciliationViewModel> _logger;
    private CancellationTokenSource _loadCts = new();
    private bool _isLoading;

    public ReconciliationViewModel(ImageMetadataExtractor extractor,
                                   ILogger<ReconciliationViewModel> logger)
    {
        _extractor = extractor;
        _logger    = logger;
    }

    public ObservableCollection<ReconciledFieldValue> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public bool HasConflicts => Fields.Any(f =>
        f.Status is MergeStatus.Conflicted or MergeStatus.Ambiguous);

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public void LoadAsync(MediaEntityRow? row)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        Fields.Clear();
        OnPropertyChanged(nameof(HasFields));
        OnPropertyChanged(nameof(HasConflicts));

        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath))
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        var path  = row.LogicalPath;
        var token = _loadCts.Token;

        // Capture filesystem and Thumbs.db dates available immediately.
        DateTime? fsLastWrite = File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : null;
        DateTime? fsCreation  = File.Exists(path)
            ? File.GetCreationTimeUtc(path)
            : null;
        DateTime? thumbsDate  = row.ThumbsDbModifiedUtc;

        _ = Task.Run(() =>
        {
            try
            {
                ExifSummary? summary = null;
                if (File.Exists(path))
                {
                    var (_, s) = _extractor.Extract(path);
                    summary = s;
                }

                if (token.IsCancellationRequested) return;

                var results = new List<ReconciledFieldValue>();

                // ── Date reconciliation ──────────────────────────────────
                var dateCandidates = new List<FieldCandidate>();

                if (summary?.CaptureDate is { } capture)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "EXIF DateTimeOriginal",
                        RawValue  = capture.ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(95)
                    });

                if (summary?.DateTimeDigitized is { } digitized)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "EXIF DateTimeDigitized",
                        RawValue  = digitized.ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(85)
                    });

                if (summary?.DateTimeModified is { } ifd0)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "EXIF DateTime (IFD0)",
                        RawValue  = ifd0.ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(70)
                    });

                if (fsLastWrite is { } fsw)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "Filesystem last-write",
                        RawValue  = fsw.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(50)
                    });

                if (fsCreation is { } fsc)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "Filesystem creation",
                        RawValue  = fsc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(40)
                    });

                if (thumbsDate is { } td)
                    dateCandidates.Add(new FieldCandidate
                    {
                        FieldName = "Date", SourceType = "Thumbs.db cache date",
                        RawValue  = td.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        Confidence = new ConfidenceScore(30)
                    });

                if (dateCandidates.Count > 0)
                {
                    var reconciled = _reconciler.Reconcile("Date", dateCandidates);

                    // Detect true conflicts: values that differ by more than 2 seconds.
                    var uniqueValues = dateCandidates
                        .Select(c => c.RawValue)
                        .Distinct()
                        .ToList();

                    if (uniqueValues.Count > 1)
                    {
                        reconciled = new ReconciledFieldValue
                        {
                            FieldName      = reconciled.FieldName,
                            PreferredValue = reconciled.PreferredValue,
                            Status         = MergeStatus.Conflicted,
                            Confidence     = reconciled.Confidence,
                            Candidates     = reconciled.Candidates
                        };
                    }

                    results.Add(reconciled);
                }

                // ── Camera model reconciliation ──────────────────────────
                if (summary?.CameraModel is { } cam)
                {
                    results.Add(_reconciler.Reconcile("Camera", [
                        new FieldCandidate
                        {
                            FieldName = "Camera", SourceType = "EXIF",
                            RawValue  = cam,
                            Confidence = new ConfidenceScore(95)
                        }
                    ]));
                }

                // ── Software reconciliation ──────────────────────────────
                if (summary?.SoftwareTag is { } sw)
                {
                    results.Add(_reconciler.Reconcile("Software", [
                        new FieldCandidate
                        {
                            FieldName = "Software", SourceType = "EXIF",
                            RawValue  = sw,
                            Confidence = new ConfidenceScore(90)
                        }
                    ]));
                }

                // ── Dimensions reconciliation ────────────────────────────
                if (summary is { Width: not null, Height: not null })
                {
                    results.Add(_reconciler.Reconcile("Dimensions", [
                        new FieldCandidate
                        {
                            FieldName = "Dimensions", SourceType = "EXIF",
                            RawValue  = $"{summary.Width}\u00d7{summary.Height}",
                            Confidence = new ConfidenceScore(99)
                        }
                    ]));
                }

                // ── Capture parameters ───────────────────────────────────
                AddSingleSource(results, "Lens", summary?.LensModel, 90);
                AddSingleSource(results, "Focal length", summary?.FocalLength, 95);
                AddSingleSource(results, "Aperture", summary?.FNumber, 95);
                AddSingleSource(results, "Exposure", summary?.ExposureTime, 95);
                if (summary?.IsoSpeed is { } iso)
                    AddSingleSource(results, "ISO", $"ISO {iso}", 95);

                // ── Display / color ──────────────────────────────────────
                AddSingleSource(results, "Color space", summary?.ColorSpace, 90);
                if (summary?.Orientation is { } orient)
                    AddSingleSource(results, "Orientation", $"EXIF {orient}", 99);

                // ── Provenance ───────────────────────────────────────────
                AddSingleSource(results, "Copyright", summary?.Copyright, 85);
                AddSingleSource(results, "Artist", summary?.Artist, 85);

                // ── XMP reconciliation ───────────────────────────────────
                // XMP CreatorTool may differ from EXIF Software — reconcile both.
                if (summary?.XmpCreatorTool is { } xmpTool &&
                    summary?.SoftwareTag is { } exifSw &&
                    !string.Equals(xmpTool, exifSw, StringComparison.OrdinalIgnoreCase))
                {
                    var softField = results.FirstOrDefault(r => r.FieldName == "Software");
                    if (softField is not null)
                    {
                        // Replace the single-source Software entry with a multi-source one.
                        results.Remove(softField);
                        var swReconciled = _reconciler.Reconcile("Software", [
                            new FieldCandidate
                            {
                                FieldName  = "Software",
                                SourceType = "EXIF IFD0",
                                RawValue   = exifSw,
                                Confidence = new ConfidenceScore(85)
                            },
                            new FieldCandidate
                            {
                                FieldName  = "Software",
                                SourceType = "XMP CreatorTool",
                                RawValue   = xmpTool,
                                Confidence = new ConfidenceScore(90)
                            }
                        ]);
                        results.Add(swReconciled);
                    }
                }

                // XMP dates feed into a broader date reconciliation — add as
                // additional candidates if present.
                if (summary?.XmpCreateDate is { } xmpCreate)
                {
                    var dateField = results.FirstOrDefault(r => r.FieldName == "Date");
                    if (dateField is not null)
                    {
                        var candidates = dateField.Candidates.ToList();
                        candidates.Add(new FieldCandidate
                        {
                            FieldName  = "Date",
                            SourceType = "XMP CreateDate",
                            RawValue   = xmpCreate.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                            Confidence = new ConfidenceScore(80)
                        });
                        if (summary?.XmpModifyDate is { } xmpMod)
                            candidates.Add(new FieldCandidate
                            {
                                FieldName  = "Date",
                                SourceType = "XMP ModifyDate",
                                RawValue   = xmpMod.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                                Confidence = new ConfidenceScore(60)
                            });

                        results.Remove(dateField);
                        var reReconciled = _reconciler.Reconcile("Date", candidates);
                        var uniqueVals = candidates.Select(c => c.RawValue).Distinct().ToList();
                        if (uniqueVals.Count > 1)
                            reReconciled = new ReconciledFieldValue
                            {
                                FieldName      = "Date",
                                PreferredValue = reReconciled.PreferredValue,
                                Status         = MergeStatus.Conflicted,
                                Confidence     = reReconciled.Confidence,
                                Candidates     = reReconciled.Candidates
                            };
                        results.Add(reReconciled);
                    }
                }

                // Depth map properties — show as a group if present.
                if (summary?.DepthFormat is not null)
                {
                    var depthDesc = summary.DepthFormat;
                    if (summary.DepthNear is not null && summary.DepthFar is not null)
                        depthDesc += $"  (near: {summary.DepthNear}, far: {summary.DepthFar})";
                    if (summary.DepthMime is not null)
                        depthDesc += $"  [{summary.DepthMime}]";
                    AddSingleSource(results, "Depth map", depthDesc, 90);
                }

                // Gain map
                AddSingleSource(results, "HDR gain map", summary?.GainMapVersion, 85);

                // Motion photo
                if (summary is { IsMotionPhoto: true })
                {
                    var mpDesc = summary.MotionPhotoVersion is not null
                        ? $"Yes (v{summary.MotionPhotoVersion})"
                        : "Yes";
                    AddSingleSource(results, "Motion photo", mpDesc, 90);
                }

                if (token.IsCancellationRequested) return;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var r in results)
                        Fields.Add(r);
                    OnPropertyChanged(nameof(HasFields));
                    OnPropertyChanged(nameof(HasConflicts));
                    IsLoading = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciliation failed: {Path}", path);
                System.Windows.Application.Current.Dispatcher.Invoke(() => IsLoading = false);
            }
        }, token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Adds a single-source resolved field when the value is non-empty.
    private void AddSingleSource(List<ReconciledFieldValue> results,
                                 string fieldName, string? value, int confidence)
    {
        if (string.IsNullOrEmpty(value)) return;
        results.Add(_reconciler.Reconcile(fieldName, [
            new FieldCandidate
            {
                FieldName  = fieldName,
                SourceType = "EXIF",
                RawValue   = value,
                Confidence = new ConfidenceScore(confidence)
            }
        ]));
    }
}
