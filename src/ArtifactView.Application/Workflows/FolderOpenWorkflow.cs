using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.ThumbCache;
using ArtifactView.Infrastructure.Sources.Android;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Cache;
using Microsoft.Extensions.Logging;

namespace ArtifactView.Application.Workflows;

public sealed class FolderOpenWorkflow
{
    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp", ".gif",
        ".webp", ".heic", ".heif", ".avif"
    };

    private static readonly HashSet<string> s_videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv"
    };

    private readonly ISourceProvider _sourceProvider;
    private readonly LocalCacheDb _cache;
    private readonly ILogger<FolderOpenWorkflow> _logger;

    public FolderOpenWorkflow(ISourceProvider sourceProvider, LocalCacheDb cache, ILogger<FolderOpenWorkflow> logger)
    {
        _sourceProvider = sourceProvider;
        _cache = cache;
        _logger = logger;
    }

    public async IAsyncEnumerable<MediaEntityRow> OpenFolderAsync(
        string folderPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Opening folder: {FolderPath}", folderPath);

        var parentPath = Directory.GetParent(folderPath)?.FullName;
        if (parentPath is not null)
        {
            yield return new MediaEntityRow
            {
                DisplayName = "..", LogicalPath = parentPath, IsDirectory = true,
                ItemIcon = "\u2B06", PresenceState = string.Empty,
                PrimarySourceType = string.Empty, ResolutionText = string.Empty,
                PreferredDateText = string.Empty, CameraModel = string.Empty,
                FindingsText = string.Empty
            };
        }

        var request = new SourceOpenRequest { Location = folderPath };
        await using var session = await _sourceProvider.OpenAsync(request, cancellationToken);

        var batch         = new List<CachedItemRecord>();
        var liveFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pre-read Thumbs.db (legacy per-folder cache).
        var thumbsDbPath  = Path.Combine(folderPath, "Thumbs.db");
        var thumbsEntries = ThumbsDbReader.ReadEntries(thumbsDbPath);
        var thumbsLookup  = new Dictionary<string, ThumbsDbEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var te in thumbsEntries)
        {
            if (!string.IsNullOrEmpty(te.OriginalFilename) && !te.OriginalFilename.StartsWith('#'))
                thumbsLookup.TryAdd(te.OriginalFilename, te);
        }

        // Pre-read ZbThumbnail.info (Zoner Photo Studio per-folder cache).
        var zbPath    = Path.Combine(folderPath, "ZbThumbnail.info");
        var zbEntries = ZbThumbnailReader.ReadEntries(zbPath);
        var zbLookup  = new Dictionary<string, ZbThumbnailEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var ze in zbEntries)
        {
            if (!string.IsNullOrEmpty(ze.OriginalFilename))
                zbLookup.TryAdd(ze.OriginalFilename, ze);
        }

        // Pre-read Windows thumbcache (Vista+, system-wide). Win8+ entries
        // have filenames that we can match against the current folder's items.
        var tcLookup = new Dictionary<string, (ThumbcacheEntry Entry, string DbPath)>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dbPath in ThumbcacheReader.DiscoverDefaultPaths())
                foreach (var entry in ThumbcacheReader.ReadEntries(dbPath))
                    if (!string.IsNullOrEmpty(entry.CacheFileName))
                        tcLookup.TryAdd(entry.CacheFileName, (entry, dbPath));
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Thumbcache discovery skipped"); }

        await foreach (var item in session.EnumerateItemsAsync(cancellationToken))
        {
            if (item.IsDirectory)
            {
                yield return new MediaEntityRow
                {
                    DisplayName = item.DisplayName, LogicalPath = item.LogicalPath,
                    IsDirectory = true, SortOrder = 1, ItemIcon = "\U0001F4C1",
                    PresenceState = string.Empty, PrimarySourceType = string.Empty,
                    ResolutionText = string.Empty, PreferredDateText = string.Empty,
                    CameraModel = string.Empty, FindingsText = string.Empty
                };
                continue;
            }

            var ext = item.Extension ?? string.Empty;
            if (!s_imageExtensions.Contains(ext) && !s_videoExtensions.Contains(ext))
                continue;

            var lastWrite = File.Exists(item.ItemId) ? (DateTime?)File.GetLastWriteTimeUtc(item.ItemId) : null;

            batch.Add(new CachedItemRecord
            {
                ItemId = item.ItemId, DisplayName = item.DisplayName,
                LogicalPath = item.LogicalPath, SizeBytes = item.Size,
                Extension = item.Extension, FileLastWriteUtc = lastWrite,
                PresenceState = "Present", PrimarySourceType = "Live file"
            });

            var hasTdb = thumbsLookup.TryGetValue(item.DisplayName, out var tMatch);
            var hasTc  = tcLookup.TryGetValue(item.DisplayName, out var tcMatch);
            var hasZb  = zbLookup.TryGetValue(item.DisplayName, out var zbMatch);

            yield return new MediaEntityRow
            {
                DisplayName       = item.DisplayName,
                LogicalPath       = item.LogicalPath,
                IsDirectory       = false,
                SortOrder         = 2,
                ItemIcon          = IconForExtension(ext),
                FileSizeText      = FormatSize(item.Size),
                FileSizeBytes     = item.Size ?? 0,
                PresenceState     = "Present",
                PrimarySourceType = "Live file",
                ThumbsDbPath        = hasTdb ? thumbsDbPath : string.Empty,
                ThumbsDbStreamName  = tMatch?.StreamName ?? string.Empty,
                ThumbsDbModifiedUtc = tMatch?.LastModifiedUtc,
                ThumbcachePath          = hasTc ? tcMatch.DbPath : string.Empty,
                ThumbcacheHash          = hasTc ? tcMatch.Entry.Hash : 0,
                ThumbcachePayloadOffset = hasTc ? tcMatch.Entry.EntryOffset + tcMatch.Entry.HeaderSize : 0,
                ThumbcacheDataSize      = hasTc ? tcMatch.Entry.DataSize : 0,
                ZbThumbnailPath          = hasZb ? zbPath : string.Empty,
                ZbThumbnailPayloadOffset = hasZb ? zbMatch!.PayloadOffset : 0,
                ZbThumbnailDataSize      = hasZb ? zbMatch!.PayloadSize : 0,
                ResolutionText    = string.Empty,
                PreferredDateText = lastWrite?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown",
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };
            liveFileNames.Add(item.DisplayName);
        }

        _cache.UpsertBatch(batch);

        // Ghost entries from Thumbs.db (legacy).
        foreach (var ghost in thumbsEntries)
        {
            if (liveFileNames.Contains(ghost.OriginalFilename)) continue;
            cancellationToken.ThrowIfCancellationRequested();

            yield return new MediaEntityRow
            {
                DisplayName       = ghost.OriginalFilename,
                LogicalPath       = Path.Combine(folderPath, ghost.OriginalFilename),
                IsDirectory       = false, SortOrder = 2,
                ItemIcon          = "\U0001F47B",
                FileSizeText      = string.Empty, FileSizeBytes = 0,
                PresenceState     = "Ghost",
                PrimarySourceType = "Thumbs.db",
                ThumbsDbPath        = thumbsDbPath,
                ThumbsDbStreamName  = ghost.StreamName,
                ThumbsDbModifiedUtc = ghost.LastModifiedUtc,
                ResolutionText    = ghost.Width > 0 && ghost.Height > 0
                    ? $"{ghost.Width}\u00d7{ghost.Height}" : string.Empty,
                PreferredDateText = ghost.LastModifiedUtc?.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                CameraModel = string.Empty, FindingsText = string.Empty
            };
        }

        // Ghost entries from Windows thumbcache (modern).
        foreach (var (name, (tcEntry, tcPath)) in tcLookup)
        {
            if (liveFileNames.Contains(name)) continue;
            if (thumbsLookup.ContainsKey(name)) continue; // already emitted as Thumbs.db ghost

            var ext = Path.GetExtension(name);
            if (!s_imageExtensions.Contains(ext) && !s_videoExtensions.Contains(ext))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            yield return new MediaEntityRow
            {
                DisplayName       = name,
                LogicalPath       = Path.Combine(folderPath, name),
                IsDirectory       = false, SortOrder = 2,
                ItemIcon          = "\U0001F47B",
                FileSizeText      = string.Empty, FileSizeBytes = 0,
                PresenceState     = "Ghost",
                PrimarySourceType = "Thumbcache",
                ThumbcachePath          = tcPath,
                ThumbcacheHash          = tcEntry.Hash,
                ThumbcachePayloadOffset = tcEntry.EntryOffset + tcEntry.HeaderSize,
                ThumbcacheDataSize      = tcEntry.DataSize,
                ResolutionText    = string.Empty,
                PreferredDateText = string.Empty,
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };
        }

        // Ghost entries from ZbThumbnail.info (Zoner Photo Studio).
        foreach (var ghost in zbEntries)
        {
            if (liveFileNames.Contains(ghost.OriginalFilename)) continue;
            if (thumbsLookup.ContainsKey(ghost.OriginalFilename)) continue;
            if (tcLookup.ContainsKey(ghost.OriginalFilename)) continue;

            var ext = Path.GetExtension(ghost.OriginalFilename);
            if (!s_imageExtensions.Contains(ext) && !s_videoExtensions.Contains(ext))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            yield return new MediaEntityRow
            {
                DisplayName       = ghost.OriginalFilename,
                LogicalPath       = Path.Combine(folderPath, ghost.OriginalFilename),
                IsDirectory       = false, SortOrder = 2,
                ItemIcon          = "\U0001F47B",
                FileSizeText      = ghost.OriginalFileSize > 0 ? FormatSize(ghost.OriginalFileSize) : string.Empty,
                FileSizeBytes     = ghost.OriginalFileSize,
                PresenceState     = "Ghost",
                PrimarySourceType = "ZbThumbnail.info",
                ZbThumbnailPath          = zbPath,
                ZbThumbnailPayloadOffset = ghost.PayloadOffset,
                ZbThumbnailDataSize      = ghost.PayloadSize,
                ResolutionText    = ghost.Width > 0 && ghost.Height > 0
                    ? $"{ghost.Width}\u00d7{ghost.Height}" : string.Empty,
                PreferredDateText = ghost.LastModifiedUtc?.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };
        }

        // Ghost entries from Android DCIM/.thumbnails/ (when browsing an Android camera folder).
        var androidThumbDir = AndroidDcimThumbnailScanner.FindThumbnailsDir(folderPath);
        if (androidThumbDir is not null)
        {
            var androidThumbs = AndroidDcimThumbnailScanner.Scan(androidThumbDir);
            foreach (var thumb in androidThumbs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip if we already have a live file with this name.
                if (thumb.OriginalFilename is not null &&
                    liveFileNames.Contains(thumb.OriginalFilename))
                    continue;

                var displayName = thumb.OriginalFilename
                    ?? Path.GetFileNameWithoutExtension(thumb.ThumbnailPath) + " (Android thumbnail)";
                var ext = Path.GetExtension(displayName);
                if (!s_imageExtensions.Contains(ext) && !s_videoExtensions.Contains(ext))
                    ext = ".jpg";

                yield return new MediaEntityRow
                {
                    DisplayName       = displayName,
                    LogicalPath       = Path.Combine(folderPath, displayName),
                    IsDirectory       = false,
                    SortOrder         = 2,
                    ItemIcon          = "\U0001F47B",
                    FileSizeText      = string.Empty,
                    FileSizeBytes     = 0,
                    PresenceState     = "Ghost",
                    PrimarySourceType = "Android .thumbnails",
                    ThumbsDbPath        = string.Empty,
                    ThumbsDbStreamName  = string.Empty,
                    ResolutionText    = thumb.Width > 0 && thumb.Height > 0
                        ? $"{thumb.Width}×{thumb.Height}" : string.Empty,
                    PreferredDateText = thumb.ThumbnailDateUtc?.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    CameraModel       = string.Empty,
                    FindingsText      = string.Empty
                };
            }
        }
    }

    private static string IconForExtension(string ext) =>
        s_imageExtensions.Contains(ext) ? "\U0001F5BC" :
        s_videoExtensions.Contains(ext) ? "\U0001F3AC" : "\U0001F4C4";

    private static string FormatSize(long? bytes) => bytes switch
    {
        null           => string.Empty,
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
