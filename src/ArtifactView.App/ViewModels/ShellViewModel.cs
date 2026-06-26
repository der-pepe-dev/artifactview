using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using ArtifactView.App.Commands;
using ArtifactView.Application.Jobs;
using ArtifactView.Application.Plugins;
using ArtifactView.Application.Settings;
using ArtifactView.Application.Workflows;
using ArtifactView.Contracts.Exporters;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Cache;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Plugins.Adapters;
using ArtifactView.Infrastructure.Reports;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;

namespace ArtifactView.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    // Raised on the UI thread when a new folder's items have been enumerated.
    // Subscribers (e.g. MainWindow) can use this to restore keyboard focus to the grid.
    public event EventHandler? FolderLoaded;
    private readonly FolderOpenWorkflow _folderOpenWorkflow;
    private readonly IPhoneBackupOpenWorkflow _backupOpenWorkflow;
    private readonly DiskImageOpenWorkflow _diskImageOpenWorkflow;
    private readonly JobScheduler _jobScheduler;
    private readonly AppSettingsStore _settingsStore;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly ImageMetadataExtractor _metadataExtractor;
    private readonly BlobStore? _blobStore;
    private readonly PluginRegistry? _pluginRegistry;
    private readonly ArtifactView.Infrastructure.Signatures.SignatureEngine _signatureEngine =
        new([new ArtifactView.Infrastructure.Signatures.CoreWorkflowSignatureRulePack()]);
    private AppSettings _settings;
    private MediaEntityRow? _selectedItem;
    private string _statusText = "Ready.";
    private string? _currentFolderPath;
    private string _searchText = string.Empty;
    private CancellationTokenSource _folderCts = new();

    // Used only for the grid-wide integrity quick-scan pass.
    private static readonly HashSet<string> s_jpegExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };
    private static readonly HashSet<string> s_pngExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png" };

    private bool _isGroupedByDate;
    private bool _isFilmstripVisible;
    private MediaEntityRow? _pinnedItem;

    // Enrichment progress tracking — updated with Interlocked so background threads are safe.
    private int _mediaCount;
    private int _enrichmentPending;
    private int _enrichmentWarnings;
    private int _enrichmentTotal;
    private bool _isEnriching;

    public ShellViewModel(
        FolderOpenWorkflow folderOpenWorkflow,
        IPhoneBackupOpenWorkflow backupOpenWorkflow,
        DiskImageOpenWorkflow diskImageOpenWorkflow,
        JobScheduler jobScheduler,
        AppSettingsStore settingsStore,
        ImageMetadataExtractor metadataExtractor,
        BlobStore? blobStore,
        ILoggerFactory loggerFactory,
        PluginRegistry? pluginRegistry = null)
    {
        _folderOpenWorkflow     = folderOpenWorkflow;
        _backupOpenWorkflow     = backupOpenWorkflow;
        _diskImageOpenWorkflow  = diskImageOpenWorkflow;
        _jobScheduler = jobScheduler;
        _settingsStore = settingsStore;
        _logger = loggerFactory.CreateLogger<ShellViewModel>();
        _metadataExtractor = metadataExtractor;
        _blobStore = blobStore;
        _pluginRegistry = pluginRegistry;
        _settings = settingsStore.Load();

        Viewer         = new ViewerViewModel(loggerFactory.CreateLogger<ViewerViewModel>());
        Metadata       = new MetadataViewModel(metadataExtractor, loggerFactory.CreateLogger<MetadataViewModel>());
        Thumbnail      = new ThumbnailViewModel(loggerFactory.CreateLogger<ThumbnailViewModel>());
        Findings       = new FindingsViewModel(metadataExtractor, loggerFactory.CreateLogger<FindingsViewModel>());
        Contributors   = new ContributorsViewModel();
        Reconstruction = new ReconstructionViewModel(metadataExtractor, blobStore, loggerFactory.CreateLogger<ReconstructionViewModel>());
        Reconciliation = new ReconciliationViewModel(metadataExtractor, loggerFactory.CreateLogger<ReconciliationViewModel>());
        RelatedItems   = new RelatedItemsViewModel    { NavigateTo = row => SelectedItem = row };
        Storyboard     = new SessionStoryboardViewModel { NavigateTo = row => SelectedItem = row };

        OpenFolderCommand          = new RelayCommand(_ => OpenFolder());
        OpenIPhoneBackupCommand    = new RelayCommand(_ => OpenIPhoneBackup());
        OpenDiskImageCommand       = new RelayCommand(_ => OpenDiskImage());
        RefreshCommand     = new RelayCommand(_ => Refresh(), _ => CurrentFolderPath is not null);
        ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty);
        NavigateCommand    = new RelayCommand(
            param =>
            {
                if (param is not MediaEntityRow row) return;
                string? selectAfterLoad = null;
                if (row.DisplayName == ".." && _currentFolderPath is not null)
                    selectAfterLoad = Path.GetFileName(_currentFolderPath);
                _ = LoadFolderAsync(row.LogicalPath, selectAfterLoad);
            },
            param => param is MediaEntityRow { IsDirectory: true });
        NavigateUpCommand  = new RelayCommand(
            param =>
            {
                var current = _currentFolderPath;
                var parent  = Path.GetDirectoryName(current);
                if (parent is not null)
                    _ = LoadFolderAsync(parent, Path.GetFileName(current));
            },
            param => _currentFolderPath is not null &&
                     Path.GetDirectoryName(_currentFolderPath) is not null);
        AnalyzeSelectedCommand = new RelayCommand(
            _ => Findings.LoadAsync(_selectedItem),
            _ => _selectedItem is { IsDirectory: false });
        OpenInExplorerCommand = new RelayCommand(
            _ =>
            {
                if (_selectedItem?.LogicalPath is { } p)
                    Process.Start("explorer.exe", $"/select,\"{p}\"");
            },
            _ => _selectedItem is not null);
        CopyPathCommand = new RelayCommand(
            _ =>
            {
                if (_selectedItem?.LogicalPath is not null)
                    System.Windows.Clipboard.SetText(_selectedItem.LogicalPath);
            },
            _ => _selectedItem?.LogicalPath is not null);
        ExportReportCommand = new RelayCommand(
            _ => ExportReport(),
            _ => _selectedItem is { IsDirectory: false } && Findings.HasFindings);
        ExportProvenanceSidecarCommand = new RelayCommand(
            _ => ExportProvenanceSidecar(),
            _ => _selectedItem is { IsDirectory: false } && !string.IsNullOrEmpty(_selectedItem.LogicalPath));
        ShowPluginsCommand = new RelayCommand(_ => ShowPlugins());

        ToggleGroupByDateCommand = new RelayCommand(_ =>
        {
            IsGroupedByDate = !IsGroupedByDate;
            var view = CollectionViewSource.GetDefaultView(Items);
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(MediaEntityRow.SortOrder), ListSortDirection.Ascending));
            if (IsGroupedByDate)
            {
                view.SortDescriptions.Add(new SortDescription(nameof(MediaEntityRow.DateGroup), ListSortDirection.Ascending));
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MediaEntityRow.DateGroup)));
            }
        });

        ToggleFilmstripCommand = new RelayCommand(_ => IsFilmstripVisible = !IsFilmstripVisible);

        PinForCompareCommand = new RelayCommand(
            _ => PinnedItem = _pinnedItem == _selectedItem ? null : _selectedItem,
            _ => _selectedItem is { IsDirectory: false });

        CompareSelectedCommand = new RelayCommand(
            _ => CompareRequested?.Invoke(this, (_pinnedItem!, _selectedItem!)),
            _ => _pinnedItem is not null && _selectedItem is not null
              && _selectedItem != _pinnedItem
              && !_selectedItem.IsDirectory && !_pinnedItem.IsDirectory);

        // Filter + initial sort:
        // The DataGrid Sorting handler keeps SortOrder as the primary description when
        // the user clicks a column header, so the grouping is preserved under any sort.
        var view = CollectionViewSource.GetDefaultView(Items);
        view.Filter = FilterItem;
        view.SortDescriptions.Add(new SortDescription(nameof(MediaEntityRow.SortOrder), ListSortDirection.Ascending));
    }

    public ViewerViewModel          Viewer         { get; }
    public MetadataViewModel        Metadata       { get; }
    public ThumbnailViewModel       Thumbnail      { get; }
    public FindingsViewModel        Findings       { get; }
    public ContributorsViewModel    Contributors   { get; }
    public ReconstructionViewModel  Reconstruction { get; }
    public ReconciliationViewModel  Reconciliation { get; }
    public RelatedItemsViewModel    RelatedItems   { get; }
    public SessionStoryboardViewModel Storyboard   { get; }

    public ObservableCollection<MediaEntityRow> Items { get; } = [];

    public MediaEntityRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            OnPropertyChanged();
            Viewer.LoadAsync(value);
            Metadata.LoadAsync(value);
            Thumbnail.LoadAsync(value);
            Findings.LoadAsync(value);
            Contributors.Load(value);
            Reconstruction.LoadAsync(value);
            Reconciliation.LoadAsync(value);
            RelatedItems.Load(value, Items);
            Storyboard.Load(value, Items);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    // True while background enrichment jobs are running. Controls status bar progress visibility.
    public bool IsEnriching
    {
        get => _isEnriching;
        private set { _isEnriching = value; OnPropertyChanged(); }
    }

    // 0–100 enrichment progress, suitable for binding to a ProgressBar.
    public double EnrichmentProgress =>
        _enrichmentTotal > 0
            ? 100.0 * (_enrichmentTotal - _enrichmentPending) / _enrichmentTotal
            : 0;

    public string? CurrentFolderPath
    {
        get => _currentFolderPath;
        private set
        {
            _currentFolderPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    // Window title: shows the open folder name next to the app name.
    // Falls back to the full path for drive roots (e.g. "C:\") where
    // GetFileName returns an empty string.
    public string WindowTitle
    {
        get
        {
            if (_currentFolderPath is null) return "ArtifactView";
            var name = Path.GetFileName(_currentFolderPath);
            return string.IsNullOrEmpty(name)
                ? $"ArtifactView  \u2014  {_currentFolderPath}"
                : $"ArtifactView  \u2014  {name}";
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(Items).Refresh();
        }
    }

    public ICommand OpenFolderCommand         { get; }
    public ICommand OpenIPhoneBackupCommand   { get; }
    public ICommand OpenDiskImageCommand      { get; }
    public ICommand RefreshCommand     { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand NavigateCommand    { get; }
    // Navigates to the parent of the current folder. Disabled at a filesystem root.
    public ICommand NavigateUpCommand      { get; }
    // Re-runs the full analysis pipeline for the selected file on demand.
    public ICommand AnalyzeSelectedCommand { get; }
    public ICommand OpenInExplorerCommand  { get; }
    public ICommand CopyPathCommand        { get; }
    public ICommand ExportReportCommand              { get; }
    public ICommand ExportProvenanceSidecarCommand   { get; }
    public ICommand ShowPluginsCommand               { get; }
    public ICommand ToggleGroupByDateCommand { get; }
    public ICommand ToggleFilmstripCommand   { get; }
    public ICommand PinForCompareCommand     { get; }
    public ICommand CompareSelectedCommand   { get; }

    public bool IsGroupedByDate
    {
        get => _isGroupedByDate;
        private set { _isGroupedByDate = value; OnPropertyChanged(); }
    }

    public bool IsFilmstripVisible
    {
        get => _isFilmstripVisible;
        private set { _isFilmstripVisible = value; OnPropertyChanged(); }
    }

    public MediaEntityRow? PinnedItem
    {
        get => _pinnedItem;
        private set
        {
            _pinnedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinBadgeText));
            ((RelayCommand)CompareSelectedCommand).RaiseCanExecuteChanged();
        }
    }

    public string PinBadgeText =>
        _pinnedItem is null ? string.Empty : $"\U0001F4CC {_pinnedItem.DisplayName}";

    // Raised when the user triggers a side-by-side compare action.
    public event EventHandler<(MediaEntityRow Left, MediaEntityRow Right)>? CompareRequested;

    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select folder to browse" };
        if (_settings.LastFolderPath is not null)
            dialog.InitialDirectory = _settings.LastFolderPath;

        if (dialog.ShowDialog() != true)
            return;

        _ = LoadFolderAsync(dialog.FolderName);
    }

    private void OpenIPhoneBackup()
    {
        // First try auto-discovery; if backups found, let user pick.
        // Fall back to manual folder select when none found.
        var discovered = Infrastructure.Sources.IPhoneBackup.IPhoneBackupDiscovery.DiscoverAll();

        string? selectedRoot = null;
        if (discovered.Count > 0)
        {
            // Build a simple prompt listing discovered backups.
            // Use OpenFolderDialog pre-seeded at the parent of the first backup.
            var parentDir = Path.GetDirectoryName(discovered[0].BackupRoot) ?? string.Empty;
            var dialog = new OpenFolderDialog
            {
                Title            = "Select iPhone Backup Folder",
                InitialDirectory = parentDir
            };
            if (dialog.ShowDialog() == true)
                selectedRoot = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFolderDialog { Title = "Select iPhone Backup Folder" };
            if (dialog.ShowDialog() == true)
                selectedRoot = dialog.FolderName;
        }

        if (selectedRoot is null) return;
        _ = LoadIPhoneBackupAsync(selectedRoot);
    }

    public Task LoadIPhoneBackupAsync(string backupRoot)
    {
        var old = _folderCts;
        _folderCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
        var token = _folderCts.Token;

        CurrentFolderPath = backupRoot;

        Items.Clear();
        if (_searchText.Length > 0)
            SearchText = string.Empty;
        StatusText = $"Loading iPhone backup…";

        return Task.Run(async () =>
        {
            var count = 0;
            try
            {
                await foreach (var row in _backupOpenWorkflow.OpenBackupAsync(backupRoot, token))
                {
                    if (!row.IsDirectory) count++;
                    var captured = row;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => Items.Add(captured));
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"{count} backup file(s) loaded");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "iPhone backup load failed: {BackupRoot}", backupRoot);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "Backup load failed");
            }
        }, token);
    }

    private void OpenDiskImage()
    {
        var dialog = new OpenFileDialog
        {
            Title  = "Open Disk Image",
            Filter = "Disk images (*.dd;*.img;*.raw;*.bin;*.iso)|*.dd;*.img;*.raw;*.bin;*.iso|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        _ = LoadDiskImageAsync(dialog.FileName);
    }

    public Task LoadDiskImageAsync(string imagePath)
    {
        var old = _folderCts;
        _folderCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
        var token = _folderCts.Token;

        CurrentFolderPath = imagePath;

        Items.Clear();
        if (_searchText.Length > 0)
            SearchText = string.Empty;
        StatusText = $"Scanning disk image…";

        return Task.Run(async () =>
        {
            var count   = 0;
            var deleted = 0;
            try
            {
                await foreach (var row in _diskImageOpenWorkflow.OpenImageAsync(imagePath, token))
                {
                    if (!row.IsDirectory)
                    {
                        count++;
                        if (row.PresenceState == "Deleted") deleted++;
                    }
                    var captured = row;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => Items.Add(captured));
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = deleted > 0
                        ? $"{count} file(s) found — {deleted} deleted"
                        : $"{count} file(s) found in disk image");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Disk image load failed: {ImagePath}", imagePath);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "Disk image load failed");
            }
        }, token);
    }

    private void Refresh()
    {
        if (CurrentFolderPath is null)
            return;

        // Remember the selected path so we can restore it after reload.
        var previousPath = _selectedItem?.LogicalPath;
        _ = LoadFolderAsync(CurrentFolderPath).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger.LogError(t.Exception, "Refresh failed for {Folder}", CurrentFolderPath);
                return;
            }
            if (previousPath is null) return;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var match = Items.FirstOrDefault(
                    r => string.Equals(r.LogicalPath, previousPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    SelectedItem = match;
            });
        }, TaskContinuationOptions.None);
    }

    private void ExportReport()
    {
        var row = _selectedItem;
        if (row is null || row.IsDirectory)
            return;

        // Gather the EXIF summary for the selected file.
        ExifSummary? summary = null;
        IReadOnlyList<RawMetadataEntry>? rawMeta = null;
        if (!string.IsNullOrEmpty(row.LogicalPath) && File.Exists(row.LogicalPath))
        {
            try
            {
                var (entries, s) = _metadataExtractor.Extract(row.LogicalPath);
                summary = s;
                rawMeta = entries;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata extraction for report failed: {Path}", row.LogicalPath);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);
        var dialog   = new SaveFileDialog
        {
            Title    = "Export findings report",
            FileName = $"{baseName}_report.txt",
            Filter   = "Text report|*.txt"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var report = FindingsReportExporter.Generate(
                row,
                summary,
                Findings.Entries.Select(e => e.Finding).ToList(),
                Contributors.Entries.ToList(),
                rawMeta,
                Reconciliation.Fields.ToList());

            File.WriteAllText(dialog.FileName, report);
            StatusText = $"Report saved: {dialog.FileName}";
            _logger.LogInformation("Report exported for {Name} → {Dest}", row.DisplayName, dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = $"Report export failed: {ex.Message}";
            _logger.LogError(ex, "Report export failed for {Path}", row.LogicalPath);
        }
    }

    private void ShowPlugins()
    {
        var pluginsFolder = _settings.PluginsDirectory;
        var vm = new PluginsViewModel(_pluginRegistry);
        vm.Refresh(pluginsFolder, _settings.PluginPolicy);

        var window = new Views.PluginsWindow(vm);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
    }

    public Task LoadFolderAsync(string folderPath, string? selectAfterLoad = null)
    {
        var old = _folderCts;
        _folderCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
        var token = _folderCts.Token;

        CurrentFolderPath = folderPath;
        _settings.LastFolderPath = folderPath;
        _settingsStore.Save(_settings);

        Items.Clear();
        if (_searchText.Length > 0)
            SearchText = string.Empty;
        StatusText = $"Loading {folderPath}…";

        return Task.Run(async () =>
        {
            var count = 0;
            try
            {
                await foreach (var row in _folderOpenWorkflow.OpenFolderAsync(folderPath, token))
                {
                    var captured = row;
                    if (!captured.IsDirectory)
                        count++;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => Items.Add(captured));
                }

                // Queue enrichment only for rows that do not yet have dimensions.
                // The selected-item path already extracts full metadata immediately,
                // so we skip rows where ResolutionText was already populated.
                var toEnrich = System.Windows.Application.Current.Dispatcher.Invoke(
                    () => Items.Where(r => !r.IsDirectory && string.IsNullOrEmpty(r.ResolutionText)).ToList());

                System.Threading.Interlocked.Exchange(ref _enrichmentPending, toEnrich.Count);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _enrichmentTotal = toEnrich.Count;
                    IsEnriching = toEnrich.Count > 0;
                    OnPropertyChanged(nameof(EnrichmentProgress));
                });

                foreach (var row in toEnrich)
                {
                    var capturedRow = row;
                    _jobScheduler.Enqueue(new BackgroundJob(
                        JobPriority.VisibleGridRows,
                        $"Enrich: {capturedRow.DisplayName}",
                        ct => EnrichRowAsync(capturedRow, ct)));
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"{count} media file(s) loaded";

                    // When navigating up, select the folder the user just left.
                    if (selectAfterLoad is not null)
                    {
                        var match = Items.FirstOrDefault(r =>
                            r.IsDirectory &&
                            string.Equals(r.DisplayName, selectAfterLoad, StringComparison.OrdinalIgnoreCase));
                        if (match is not null)
                            SelectedItem = match;
                    }

                    FolderLoaded?.Invoke(this, EventArgs.Empty);
                });

                // Reset enrichment counters for this folder load.
                _mediaCount      = count;
                _enrichmentWarnings = 0;
            }
            catch (OperationCanceledException)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "Load cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load folder: {FolderPath}", folderPath);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "Error loading folder — see log.");
            }
        }, token);
    }

    private async Task EnrichRowAsync(MediaEntityRow row, CancellationToken ct)
    {
        if (!File.Exists(row.LogicalPath))
        {
            System.Threading.Interlocked.Decrement(ref _enrichmentPending);
            return;
        }
        try
        {
            var path = row.LogicalPath;
            var ext  = Path.GetExtension(path);
            var findings = new List<Finding>();

            var (_, summary) = _metadataExtractor.Extract(path);
            if (ct.IsCancellationRequested)
                return;

            // ── Integrity ────────────────────────────────────────────────
            if (s_jpegExtensions.Contains(ext))
                findings.AddRange(JpegIntegrityAnalyzer.Analyze(path));
            else if (s_pngExtensions.Contains(ext))
                findings.AddRange(PngIntegrityAnalyzer.Analyze(path));

            // ── Format mismatch ──────────────────────────────────────────
            var formatFinding = FormatMismatchAnalyzer.Analyze(path);
            if (formatFinding is not null)
                findings.Add(formatFinding);

            var detected = MagicByteFormatDetector.Detect(path);
            var expected = MagicByteFormatDetector.ExpectedFormatForExtension(ext);
            var formatMismatch = detected is not null && expected is not null && detected != expected;

            if (ct.IsCancellationRequested)
                return;

            // ── Embedded artifacts ───────────────────────────────────────
            if (s_jpegExtensions.Contains(ext))
                EmbeddedArtifactFindingsBuilder.AddFindings(
                    JpegEmbeddedArtifactScanner.Scan(path), findings);


            // ── File hash + perceptual hash ──────────────────────────────
            var hashFinding = FileHashAnalyzer.Analyze(path);
            findings.Add(hashFinding);
            var hashHex = hashFinding.SupportingFactors.Count > 0 ? hashFinding.SupportingFactors[0] : string.Empty;
            var dHash   = DHashComputer.ComputeFromFile(path);

            // ── Signature recognition ────────────────────────────────────
            var signatureResult = _signatureEngine.Run(
                new ArtifactView.Infrastructure.Signatures.ExifSignatureContext(summary, path));
            var workflowBadge = signatureResult.TopMatch?.ProfileName ?? string.Empty;
            ArtifactView.Infrastructure.Signatures.SignatureFindingsBuilder
                .AddFindings(signatureResult, findings);

            // ── Software + timestamp consistency ─────────────────────────
            try
            {
                if (summary.SoftwareTag is not null)
                    findings.AddRange(SoftwareAnalyzer.Analyze(summary.SoftwareTag));

                if (summary.CaptureDate.HasValue)
                    findings.AddRange(TimestampConsistencyAnalyzer.Analyze(
                        path,
                        summary.CaptureDate,
                        summary.DateTimeDigitized,
                        summary.DateTimeModified));

                // Thumbnail presence (aspect ratio check without decoding).
                if (summary.HasThumbnail && summary.Width.HasValue && summary.Height.HasValue
                    && summary.ThumbnailHeight > 0 && summary.Height.Value > 0)
                {
                    var thumbRatio = (double)summary.ThumbnailWidth!.Value / summary.ThumbnailHeight!.Value;
                    var mainRatio  = (double)summary.Width.Value / summary.Height.Value;
                    var ratioDiff  = Math.Abs(thumbRatio - mainRatio) / mainRatio;

                    if (ratioDiff > 0.02)
                    {
                        findings.Add(new Finding
                        {
                            Id = "thumb-aspect-ratio-mismatch",
                            Category = "Thumbnail",
                            ReviewPriority = ReviewPriority.Medium,
                            Observation =
                                $"Thumbnail aspect ratio ({summary.ThumbnailWidth}\u00d7{summary.ThumbnailHeight} \u2192 " +
                                $"{thumbRatio:F3}) differs from main image ({summary.Width}\u00d7{summary.Height} " +
                                $"\u2192 {mainRatio:F3}).",
                            ObservationConfidence = new ConfidenceScore(99),
                            Interpretation =
                                "Consistent with cropping or resizing after the original " +
                                "thumbnail was written.",
                            InterpretationConfidence = new ConfidenceScore(75)
                        });
                    }
                    else
                    {
                        findings.Add(new Finding
                        {
                            Id = "thumb-aspect-ratio-match",
                            Category = "Thumbnail",
                            ReviewPriority = ReviewPriority.None,
                            Observation =
                                $"Thumbnail aspect ratio ({summary.ThumbnailWidth}\u00d7{summary.ThumbnailHeight} \u2192 " +
                                $"{thumbRatio:F3}) matches main image ({summary.Width}\u00d7{summary.Height} " +
                                $"\u2192 {mainRatio:F3}).",
                            ObservationConfidence = new ConfidenceScore(95)
                        });
                    }
                }
                else if (summary.Width.HasValue && !summary.HasThumbnail)
                {
                    findings.Add(new Finding
                    {
                        Id = "thumb-absent",
                        Category = "Thumbnail",
                        ReviewPriority = ReviewPriority.None,
                        Observation = "No embedded EXIF thumbnail found.",
                        ObservationConfidence = new ConfidenceScore(99)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Partial analysis for {Name}", row.DisplayName);
            }

            // ── Thumbs.db date cross-check ───────────────────────────────
            if (!string.IsNullOrEmpty(row.ThumbsDbPath) &&
                !string.IsNullOrEmpty(row.ThumbsDbStreamName) &&
                row.ThumbsDbModifiedUtc.HasValue)
            {
                try
                {
                    var currentWrite = File.GetLastWriteTimeUtc(path);
                    var cacheDelta   = currentWrite - row.ThumbsDbModifiedUtc.Value;

                    if (Math.Abs(cacheDelta.TotalSeconds) < 2)
                    {
                        findings.Add(new Finding
                        {
                            Id                    = "thumbsdb-date-match",
                            Category              = "Thumbs.db",
                            ReviewPriority        = ReviewPriority.None,
                            Observation           = $"File last-write ({currentWrite:yyyy-MM-dd HH:mm:ss} UTC) matches Thumbs.db cached date.",
                            ObservationConfidence = new ConfidenceScore(95)
                        });
                    }
                    else
                    {
                        var direction = cacheDelta.TotalSeconds > 0 ? "newer" : "older";
                        findings.Add(new Finding
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
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Thumbs.db date check skipped for {Name}", row.DisplayName);
                }
            }

            var warnCount = findings.Count(f => f.ReviewPriority >= ReviewPriority.Medium);
            if (warnCount > 0)
                System.Threading.Interlocked.Increment(ref _enrichmentWarnings);

            if (ct.IsCancellationRequested)
                return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (summary.Width.HasValue && summary.Height.HasValue)
                    row.ResolutionText = $"{summary.Width}\u00d7{summary.Height}";
                if (summary.CaptureDate.HasValue)
                {
                    row.PreferredDateText = summary.CaptureDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    row.DateGroup = summary.CaptureDate.Value.ToLocalTime().ToString("yyyy-MM-dd");
                }
                if (summary.CameraModel is not null)
                    row.CameraModel = summary.CameraModel;
                if (summary.GpsText is not null)
                    row.GpsText = summary.GpsText;

                // Format column: show detected format with ⚠ when it mismatches the extension.
                if (detected is not null)
                    row.DetectedFormat = formatMismatch
                        ? $"{detected} \u26a0"
                        : detected.ToString()!;

                row.Sha256Hash          = hashHex;
                row.PerceptualHashValue = dHash?.Value ?? 0UL;
                row.CachedFindings      = findings;
                row.FindingsText        = warnCount > 0 ? $"{warnCount} \u26a0" : "\u2713";
                if (!string.IsNullOrEmpty(workflowBadge))
                    row.WorkflowBadge = workflowBadge;
            }, System.Windows.Threading.DispatcherPriority.Background, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping enrichment for {Name}", row.DisplayName);
        }
        finally
        {
            // Always decrement so the "all done" check can fire even after exceptions.
            if (!ct.IsCancellationRequested)
            {
                var remaining = System.Threading.Interlocked.Decrement(ref _enrichmentPending);

                // Post a progress update every 5 items and on completion to avoid
                // flooding the UI thread during large folder scans.
                if (remaining == 0 || remaining % 5 == 0)
                {
                    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        OnPropertyChanged(nameof(EnrichmentProgress));

                        if (remaining == 0)
                        {
                            IsEnriching = false;
                            var warnings = _enrichmentWarnings;
                            var total    = _mediaCount;
                            StatusText = warnings > 0
                                ? $"{total} file(s) \u2014 {warnings} finding warning(s)"
                                : $"{total} file(s) \u2014 all clean";

                            RunDuplicateDetectionPass();
                            RunNearDuplicatePass();
                            RunSequenceGapPass();
                            RunBurstSessionClusteringPass();
                            RunSessionOutlierPass();
                            RunAppDbCorrelationPass();
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
    }

    // Groups enriched items by SHA-256 hash and annotates duplicates.
    // Runs on the UI thread after all enrichment jobs complete.
    private void RunDuplicateDetectionPass()
    {
        var fileHashes = Items
            .Where(r => !r.IsDirectory && !string.IsNullOrEmpty(r.Sha256Hash))
            .Select(r => (r.LogicalPath, r.Sha256Hash))
            .ToList();

        if (fileHashes.Count == 0) return;

        var groups = ExactDuplicateDetector.Detect(fileHashes);
        if (groups.Count == 0) return;

        // Build path → group lookup for O(1) annotation.
        var lookup = new Dictionary<string, DuplicateGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var p in g.Paths)
                lookup[p] = g;

        var dupTotal = 0;
        foreach (var row in Items)
        {
            if (!lookup.TryGetValue(row.LogicalPath ?? string.Empty, out var group)) continue;

            // DuplicateCount = number of OTHER files with the same hash.
            row.DuplicateCount = group.Paths.Count - 1;
            dupTotal++;

            // Append a duplicate finding so it appears in the Findings tab.
            var others = group.Paths
                .Where(p => !string.Equals(p, row.LogicalPath, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .ToList();
            var finding = new Finding
            {
                Id       = "file-exact-duplicate",
                Category = "Provenance",
                ReviewPriority = ReviewPriority.Medium,
                Observation =
                    $"Exact duplicate — {group.Paths.Count - 1} other file(s) in this folder " +
                    $"share the same SHA-256 hash.",
                ObservationConfidence = new ConfidenceScore(99),
                Interpretation =
                    "Identical content may indicate copied/renamed files, camera duplicate " +
                    "protection copies, or backup files in the same directory.",
                InterpretationConfidence = new ConfidenceScore(75),
                SupportingFactors = others!
            };

            AppendFinding(row, finding);
        }

        if (dupTotal > 0)
        {
            var prev = StatusText;
            StatusText = $"{prev} — {dupTotal} exact duplicate(s) detected";
            System.Threading.Interlocked.Add(ref _enrichmentWarnings, dupTotal);
        }
    }

    // Compares perceptual hashes (dHash) across all enriched rows.
    // Rows that are visually similar but not byte-identical → "near-duplicate" finding.
    // Skips rows already flagged as exact duplicates to avoid double-reporting.
    private void RunNearDuplicatePass()
    {
        var fileHashes = Items
            .Where(r => !r.IsDirectory && r.PerceptualHashValue != 0)
            .Select(r => (r.LogicalPath, new PerceptualHash(r.PerceptualHashValue)))
            .ToList();

        if (fileHashes.Count < 2) return;

        var groups = NearDuplicateDetector.Detect(fileHashes);
        if (groups.Count == 0) return;

        var rowByPath = Items
            .Where(r => !string.IsNullOrEmpty(r.LogicalPath))
            .ToDictionary(r => r.LogicalPath, StringComparer.OrdinalIgnoreCase);

        var nearDupTotal = 0;
        foreach (var group in groups)
        {
            foreach (var (memberPath, _) in group.Members)
            {
                if (!rowByPath.TryGetValue(memberPath, out var row)) continue;

                // Skip if already flagged as exact duplicate — near-dup is weaker evidence.
                if (row.DuplicateCount > 0) continue;

                var others = group.Members
                    .Where(m => !string.Equals(m.Path, memberPath, StringComparison.OrdinalIgnoreCase))
                    .Select(m => Path.GetFileName(m.Path))
                    .ToList();

                AppendFinding(row, new Finding
                {
                    Id       = "file-near-duplicate",
                    Category = "Provenance",
                    ReviewPriority = ReviewPriority.Low,
                    Observation =
                        $"Visually similar to {group.Members.Count - 1} other file(s) in this folder " +
                        $"(dHash distance ≤ {PerceptualHash.NearDuplicateThreshold}).",
                    ObservationConfidence    = new ConfidenceScore(70),
                    Interpretation =
                        "Consistent with JPEG recompression, slight edits (brightness/crop), " +
                        "or photos of the same scene. Not an exact byte-level duplicate.",
                    InterpretationConfidence = new ConfidenceScore(55),
                    SupportingFactors        = others!
                });

                nearDupTotal++;
            }
        }

        if (nearDupTotal > 0)
            StatusText += $" — {nearDupTotal} near-duplicate(s)";
    }

    // Detects gaps in camera sequence numbers (IMG_0001 → IMG_0010 skips 8 frames).
    // Runs on the UI thread after enrichment.  Attaches findings to the two rows
    // that bracket each gap so the analyst sees the anomaly in context.
    private void RunSequenceGapPass()
    {
        var files = Items
            .Where(r => !r.IsDirectory && !string.IsNullOrEmpty(r.LogicalPath))
            .Select(r => (r.LogicalPath, r.DisplayName))
            .ToList();

        if (files.Count < 2) return;

        var gaps = SequenceGapDetector.Detect(files);
        if (gaps.Count == 0) return;

        var rowByPath = Items
            .Where(r => !string.IsNullOrEmpty(r.LogicalPath))
            .ToDictionary(r => r.LogicalPath, StringComparer.OrdinalIgnoreCase);

        foreach (var gap in gaps)
        {
            var afterFinding = new Finding
            {
                Id       = "sequence-gap-after",
                Category = "Provenance",
                ReviewPriority = ReviewPriority.High,
                Observation =
                    $"Camera sequence gap: {gap.MissingCount} frame(s) missing between " +
                    $"{gap.Prefix}{gap.LastBefore} and {gap.Prefix}{gap.FirstAfter}.",
                ObservationConfidence    = new ConfidenceScore(80),
                Interpretation =
                    "Consecutive sequence numbers are absent. Possible causes: photos deleted " +
                    "from the camera or card after transfer, removed by a third party, or " +
                    "camera sequence rollover (less likely for large gaps).",
                InterpretationConfidence = new ConfidenceScore(60),
                SupportingFactors =
                [
                    $"Last present: {gap.Prefix}{gap.LastBefore} ({Path.GetFileName(gap.PathBefore)})",
                    $"First after gap: {gap.Prefix}{gap.FirstAfter} ({Path.GetFileName(gap.PathAfter)})",
                    $"Missing count: {gap.MissingCount}"
                ]
            };

            if (rowByPath.TryGetValue(gap.PathAfter, out var afterRow))
                AppendFinding(afterRow, afterFinding);
        }

        var gapTotal = gaps.Sum(g => g.MissingCount);
        StatusText += $" — {gaps.Count} sequence gap(s) ({gapTotal} missing frame(s))";
    }

    // Assigns BurstId and SessionId to rows based on capture-time proximity.
    // Runs on the UI thread after enrichment.
    private void RunBurstSessionClusteringPass()
    {
        var inputs = Items
            .Where(r => !r.IsDirectory && !string.IsNullOrEmpty(r.LogicalPath))
            .Select(r =>
            {
                // Prefer CaptureDate from findings, fall back to CachedFindings timestamp context.
                // PreferredDateText is a display string; parse it for clustering.
                DateTime? ts = null;
                if (!string.IsNullOrEmpty(r.PreferredDateText) &&
                    DateTime.TryParse(r.PreferredDateText, out var parsed))
                    ts = parsed;
                return (r.LogicalPath, ts);
            })
            .ToList();

        if (inputs.Count < 2) return;

        var assignments = BurstSessionClusterer.Cluster(inputs);
        var lookup = assignments.ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
        var rowByPath = Items
            .Where(r => !string.IsNullOrEmpty(r.LogicalPath))
            .ToDictionary(r => r.LogicalPath, StringComparer.OrdinalIgnoreCase);

        foreach (var assign in assignments)
        {
            if (!rowByPath.TryGetValue(assign.Path, out var row)) continue;
            row.BurstId   = assign.BurstId;
            row.SessionId = assign.SessionId;
        }

        // Refresh storyboard now that SessionIds are assigned.
        Storyboard.Load(_selectedItem, Items);
    }

    // Detects files whose timestamp or camera model are anomalous relative to
    // the rest of the folder.  Runs after burst clustering so session IDs are set.
    private void RunSessionOutlierPass()
    {
        var inputs = Items
            .Where(r => !r.IsDirectory && !string.IsNullOrEmpty(r.LogicalPath))
            .Select(r =>
            {
                DateTime? ts = null;
                if (!string.IsNullOrEmpty(r.PreferredDateText) &&
                    DateTime.TryParse(r.PreferredDateText, out var parsed))
                    ts = parsed;
                var model = string.IsNullOrWhiteSpace(r.CameraModel) ? null : r.CameraModel;
                return (r.LogicalPath, ts, model);
            })
            .ToList();

        if (inputs.Count < 2) return;

        var outliers = SessionOutlierDetector.Detect(inputs);
        if (outliers.Count == 0) return;

        var rowByPath = Items
            .Where(r => !string.IsNullOrEmpty(r.LogicalPath))
            .ToDictionary(r => r.LogicalPath, StringComparer.OrdinalIgnoreCase);

        foreach (var outlier in outliers)
        {
            if (!rowByPath.TryGetValue(outlier.Path, out var row)) continue;

            AppendFinding(row, new Finding
            {
                Id       = "session-outlier",
                Category = "Session Context",
                ReviewPriority           = ReviewPriority.Low,
                Observation              = string.Join(" ", outlier.Reasons),
                ObservationConfidence    = new ConfidenceScore(70),
                Interpretation           = "Consistent with a file from a different capture session, " +
                                           "device, or time period inserted into this folder. " +
                                           "May be benign (mixed events, borrowed device) or significant.",
                InterpretationConfidence = new ConfidenceScore(45),
            });
        }

        _logger.LogDebug("Session outlier pass: {Count} outlier(s) detected", outliers.Count);
    }

    // Checks for known app databases (WhatsApp, Telegram, Signal) in the current folder
    // and annotates matching media rows with correlation findings.
    private void RunAppDbCorrelationPass()
    {
        if (_currentFolderPath is null) return;

        var liveRows = Items
            .Where(r => !r.IsDirectory && r.PresenceState == "Present")
            .ToList();
        if (liveRows.Count == 0) return;

        var filenames = liveRows
            .Select(r => r.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var correlator = new Infrastructure.Sources.AppDb.AppDbCorrelator();
        var entries    = correlator.Correlate(_currentFolderPath, filenames);
        if (entries.Count == 0) return;

        var byFilename = liveRows.ToDictionary(r => r.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!byFilename.TryGetValue(entry.MediaFilename, out var row)) continue;

            var priority = entry.Confidence switch
            {
                Infrastructure.Sources.AppDb.AppDbCorrelationConfidence.High   => ReviewPriority.Low,
                Infrastructure.Sources.AppDb.AppDbCorrelationConfidence.Medium => ReviewPriority.None,
                _                                                               => ReviewPriority.None
            };

            AppendFinding(row, new Finding
            {
                Id       = $"app-db-correlation-{entry.AppName.ToLowerInvariant()}",
                Category = "App DB Correlation",
                ReviewPriority           = priority,
                Observation              = entry.Summary,
                ObservationConfidence    = new ConfidenceScore(75),
                Interpretation           = $"File is referenced in a {entry.AppName} database " +
                                           "found in this folder. Consistent with media shared " +
                                           $"through or managed by {entry.AppName}.",
                InterpretationConfidence = new ConfidenceScore(65)
            });
        }

        _logger.LogDebug("App DB correlation pass: {Count} match(es) found", entries.Count);
    }

    // Appends a finding to a row's CachedFindings and updates the badge.
    private static void AppendFinding(MediaEntityRow row, Finding finding)
    {
        if (row.CachedFindings is null)
            row.CachedFindings = [finding];
        else
            row.CachedFindings = [..row.CachedFindings, finding];

        var warnCount = row.CachedFindings.Count(f => f.ReviewPriority >= ReviewPriority.Medium);
        row.FindingsText = warnCount > 0 ? $"{warnCount} ⚠" : "✓";
    }

    // Writes a <name>.avmeta.json provenance sidecar alongside the source file.
    // Uses FindingsReportJsonExporterPlugin so the output format stays in sync with
    // the plugin's JSON schema.  Does not show a dialog — the destination is fixed
    // to avoid confusion about where the file lands.
    private void ExportProvenanceSidecar()
    {
        var row = _selectedItem;
        if (row is null || string.IsNullOrEmpty(row.LogicalPath)) return;

        ExifSummary? summary = null;
        if (File.Exists(row.LogicalPath))
        {
            try { (_, summary) = _metadataExtractor.Extract(row.LogicalPath); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata extraction for sidecar failed: {Path}", row.LogicalPath);
            }
        }

        var dir      = Path.GetDirectoryName(row.LogicalPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(row.DisplayName);
        var destPath = Path.Combine(dir, $"{baseName}.avmeta.json");

        var services = new SimpleServiceProvider(new Dictionary<Type, object?>
        {
            [typeof(MediaEntityRow)]                    = row,
            [typeof(ExifSummary)]                       = summary,
            [typeof(IReadOnlyList<Finding>)]            = (IReadOnlyList<Finding>)Findings.Entries.ToList(),
            [typeof(IReadOnlyList<EvidenceContributor>)] = (IReadOnlyList<EvidenceContributor>)Contributors.Entries.ToList(),
            [typeof(IReadOnlyList<ReconciledFieldValue>)] = (IReadOnlyList<ReconciledFieldValue>)Reconciliation.Fields.ToList()
        });

        var context = new SimpleExportContext(row.LogicalPath, destPath, services);
        var plugin  = new FindingsReportJsonExporterPlugin();

        var result = plugin.ExportAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        StatusText = result.Success
            ? $"Provenance sidecar saved: {Path.GetFileName(destPath)}"
            : $"Sidecar export failed: {result.ErrorMessage}";
    }

    private sealed record SimpleExportContext(
        string ItemId, string DestinationPath, IServiceProvider Services) : IExportContext
    {
        public IReadOnlyDictionary<string, string> Options => new Dictionary<string, string>();
    }

    private sealed class SimpleServiceProvider(Dictionary<Type, object?> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.TryGetValue(serviceType, out var svc) ? svc : null;
    }

    private bool FilterItem(object obj) =>
        obj is MediaEntityRow row &&
        (row.IsDirectory ||
         string.IsNullOrEmpty(_searchText) ||
         row.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        _folderCts.Cancel();
        _folderCts.Dispose();
        (Findings   as IDisposable)?.Dispose();
        (Thumbnail  as IDisposable)?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
