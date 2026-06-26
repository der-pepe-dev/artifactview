using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Sources.DiskImage;
using Microsoft.Extensions.Logging;

namespace ArtifactView.Application.Workflows;

public sealed class DiskImageOpenWorkflow(ILogger<DiskImageOpenWorkflow> logger)
{
    public async IAsyncEnumerable<MediaEntityRow> OpenImageAsync(
        string imagePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Opening disk image: {ImagePath}", imagePath);

        var entries = await Task.Run(
            () => DiskImagePartitionReader.ReadAllMediaFiles(imagePath), cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext      = Path.GetExtension(entry.LogicalPath);
            var isGhost  = entry.IsDeleted;
            var dateText = (entry.ModifiedUtc ?? entry.CreatedUtc)?.ToLocalTime()
                               .ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

            yield return new MediaEntityRow
            {
                DisplayName       = Path.GetFileName(entry.LogicalPath),
                LogicalPath       = string.Empty, // bytes not directly accessible without carving
                IsDirectory       = false,
                SortOrder         = 2,
                ItemIcon          = isGhost ? "\U0001F47B" : IconForExtension(ext),
                FileSizeText      = isGhost ? string.Empty : FormatSize(entry.SizeBytes),
                FileSizeBytes     = entry.SizeBytes,
                PresenceState     = isGhost ? "Deleted" : "Disk Image",
                PrimarySourceType = $"{entry.Filesystem} (partition {entry.PartitionIndex + 1})",
                ResolutionText    = string.Empty,
                PreferredDateText = dateText,
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };

            await Task.Yield();
        }
    }

    private static string IconForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".heic" or ".heif" or ".png"
            or ".gif" or ".bmp" or ".tif" or ".tiff"
            or ".webp" or ".avif" or ".dng" or ".cr2"
            or ".cr3" or ".nef" or ".arw" or ".raf" => "\U0001F5BC",
        ".mp4" or ".mov" or ".m4v" or ".3gp"
            or ".avi" or ".mkv"                      => "\U0001F3AC",
        _                                            => "\U0001F4C4"
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
