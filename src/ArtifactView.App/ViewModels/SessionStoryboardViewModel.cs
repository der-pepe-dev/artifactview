using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ArtifactView.App.Commands;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.App.ViewModels;

public sealed class StoryboardItemViewModel : INotifyPropertyChanged
{
    public required string         DisplayName       { get; init; }
    public required string         LogicalPath       { get; init; }
    public required string         PreferredDateText { get; init; }
    public required bool           IsCurrentItem     { get; init; }
    public required MediaEntityRow Row               { get; init; }

    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumbnail)); }
    }

    public bool HasThumbnail => _thumbnail is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SessionStoryboardViewModel : INotifyPropertyChanged, IDisposable
{
    private string       _sessionLabel = string.Empty;
    private CancellationTokenSource _loadCts = new();

    public ObservableCollection<StoryboardItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public string SessionLabel
    {
        get => _sessionLabel;
        private set { _sessionLabel = value; OnPropertyChanged(); }
    }

    public ICommand NavigateToItemCommand { get; }

    public Action<MediaEntityRow>? NavigateTo { get; set; }

    public SessionStoryboardViewModel()
    {
        NavigateToItemCommand = new RelayCommand(
            param => { if (param is StoryboardItemViewModel vm) NavigateTo?.Invoke(vm.Row); },
            param => param is StoryboardItemViewModel { IsCurrentItem: false });
    }

    public void Load(MediaEntityRow? selected, IEnumerable<MediaEntityRow> allRows)
    {
        var old = _loadCts;
        _loadCts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();

        Items.Clear();
        SessionLabel = string.Empty;

        if (selected is null || selected.IsDirectory)
        {
            OnPropertyChanged(nameof(HasItems));
            return;
        }

        var sessionId = selected.SessionId;
        var burstId   = selected.BurstId;

        List<MediaEntityRow> members;

        if (sessionId > 0)
        {
            members = allRows
                .Where(r => !r.IsDirectory && r.SessionId == sessionId)
                .OrderBy(r => r.PreferredDateText)
                .ThenBy(r => r.DisplayName)
                .ToList();
        }
        else if (burstId > 0)
        {
            // Fall back to burst if no session assigned.
            members = allRows
                .Where(r => !r.IsDirectory && r.BurstId == burstId)
                .OrderBy(r => r.PreferredDateText)
                .ThenBy(r => r.DisplayName)
                .ToList();
        }
        else
        {
            OnPropertyChanged(nameof(HasItems));
            return;
        }

        if (members.Count < 2)
        {
            OnPropertyChanged(nameof(HasItems));
            return;
        }

        var label = sessionId > 0
            ? $"Session {sessionId} · {members.Count} file(s)"
            : $"Burst {burstId} · {members.Count} file(s)";
        SessionLabel = label;

        var token     = _loadCts.Token;
        var viewItems = members
            .Select(r => new StoryboardItemViewModel
            {
                DisplayName       = r.DisplayName,
                LogicalPath       = r.LogicalPath ?? string.Empty,
                PreferredDateText = r.PreferredDateText,
                IsCurrentItem     = ReferenceEquals(r, selected),
                Row               = r
            })
            .ToList();

        foreach (var item in viewItems)
            Items.Add(item);

        OnPropertyChanged(nameof(HasItems));

        // Load thumbnails asynchronously for each item.
        _ = Task.Run(() => LoadThumbnailsAsync(viewItems, members, token), token);
    }

    private async Task LoadThumbnailsAsync(
        IReadOnlyList<StoryboardItemViewModel> viewItems,
        IReadOnlyList<MediaEntityRow>          rows,
        CancellationToken                      token)
    {
        for (var i = 0; i < viewItems.Count; i++)
        {
            if (token.IsCancellationRequested) return;

            var item = viewItems[i];
            var row  = rows[i];
            var src  = await LoadOneThumbnailAsync(row, token).ConfigureAwait(false);

            if (token.IsCancellationRequested || src is null) continue;

            var capture = item;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested)
                    capture.Thumbnail = src;
            });
        }
    }

    // Tries, in order: cache thumbnail, live file decode (DecodePixelWidth=120).
    // Returns null on any failure — storyboard shows placeholder instead.
    private static async Task<BitmapSource?> LoadOneThumbnailAsync(
        MediaEntityRow row, CancellationToken token)
    {
        return await Task.Run<BitmapSource?>(() =>
        {
            if (token.IsCancellationRequested) return null;

            // 1. Thumbs.db
            if (!string.IsNullOrEmpty(row.ThumbsDbPath) &&
                !string.IsNullOrEmpty(row.ThumbsDbStreamName))
            {
                try
                {
                    var entry   = new ThumbsDbEntry(row.DisplayName, 0, 0, 0, 0,
                        row.ThumbsDbStreamName, null, 0);
                    var payload = ThumbsDbReader.ExtractPayload(row.ThumbsDbPath, entry);
                    if (payload is { Length: > 0 })
                    {
                        using var ms  = new MemoryStream(payload);
                        var frame = BitmapFrame.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                        frame.Freeze();
                        return frame;
                    }
                }
                catch { /* fall through */ }
            }

            // 2. Thumbcache
            if (!string.IsNullOrEmpty(row.ThumbcachePath) && row.ThumbcacheDataSize > 0)
            {
                try
                {
                    var payload = ThumbcacheReader.ExtractPayloadDirect(
                        row.ThumbcachePath, row.ThumbcachePayloadOffset, row.ThumbcacheDataSize);
                    if (payload is { Length: > 0 })
                    {
                        using var ms  = new MemoryStream(payload);
                        var frame = BitmapFrame.Create(ms,
                            BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                        frame.Freeze();
                        return frame;
                    }
                }
                catch { /* fall through */ }
            }

            // 3. Live file — downsampled to 120px wide for performance.
            if (!string.IsNullOrEmpty(row.LogicalPath) && File.Exists(row.LogicalPath))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource        = new Uri(row.LogicalPath, UriKind.Absolute);
                    bi.DecodePixelWidth = 120;
                    bi.CacheOption      = BitmapCacheOption.OnLoad;
                    bi.CreateOptions    = BitmapCreateOptions.IgnoreColorProfile;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch { /* fall through */ }
            }

            return null;
        }, token).ConfigureAwait(false);
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
