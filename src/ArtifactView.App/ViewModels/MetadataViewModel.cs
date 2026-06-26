using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Metadata;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App.ViewModels;

public sealed class MetadataViewModel : INotifyPropertyChanged
{
    private readonly ImageMetadataExtractor _extractor;
    private readonly ILogger<MetadataViewModel> _logger;
    private bool _isLoading;
    private CancellationTokenSource _loadCts = new();

    public MetadataViewModel(ImageMetadataExtractor extractor, ILogger<MetadataViewModel> logger)
    {
        _extractor = extractor;
        _logger    = logger;
    }

    public ObservableCollection<RawMetadataEntry> Entries { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasEntries)); }
    }

    public bool HasEntries => Entries.Count > 0;

    // Called on the UI thread from ShellViewModel.SelectedItem.set.
    public void LoadAsync(MediaEntityRow? row)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        Entries.Clear();
        OnPropertyChanged(nameof(HasEntries));

        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath) || !File.Exists(row.LogicalPath))
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        var path  = row.LogicalPath;
        var token = _loadCts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                var (entries, summary) = _extractor.Extract(path);
                if (token.IsCancellationRequested)
                    return;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var entry in entries)
                        Entries.Add(entry);
                    OnPropertyChanged(nameof(HasEntries));

                    // Enrich the still-selected row with the extracted key fields.
                    if (summary.Width.HasValue && summary.Height.HasValue)
                        row.ResolutionText = $"{summary.Width}\u00d7{summary.Height}";
                    if (summary.CaptureDate.HasValue)
                        row.PreferredDateText = summary.CaptureDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    if (summary.CameraModel is not null)
                        row.CameraModel = summary.CameraModel;
                    if (summary.GpsText is not null)
                        row.GpsText = summary.GpsText;

                    IsLoading = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract metadata: {Path}", path);
                System.Windows.Application.Current.Dispatcher.Invoke(() => IsLoading = false);
            }
        }, token);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
