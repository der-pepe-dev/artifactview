using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Sources.Carving;
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
                // Live files are read by internal path; deleted NTFS files are recovered
                // from their $MFT record number (best-effort). Both need the image path.
                DiskImagePath           = imagePath,
                DiskImagePartitionIndex = entry.PartitionIndex,
                DiskImageInternalPath   = isGhost ? string.Empty : entry.LogicalPath,
                DiskImageFilesystem     = entry.Filesystem,
                DeletedMftRecordNumber  = entry.MftRecordNumber,
                ResolutionText    = string.Empty,
                PreferredDateText = dateText,
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };

            await Task.Yield();
        }

        // Carving pass: recover signature-carved artifacts (JPEG/PNG) from the raw image,
        // including content with no live filesystem entry. Surfaced as "Carved" rows whose
        // bytes the viewer reads directly from the carved [offset, +length) range.
        var carved = await Task.Run(() => SignatureCarver.CarveFile(imagePath), cancellationToken);
        foreach (var c in carved)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new MediaEntityRow
            {
                DisplayName       = $"carved_{c.Offset:x}{c.Extension}",
                LogicalPath       = string.Empty, // recoverable via the carved byte range, not a path
                IsDirectory       = false,
                SortOrder         = 3,
                ItemIcon          = IconForExtension(c.Extension),
                FileSizeText      = FormatSize(c.Length),
                FileSizeBytes     = c.Length,
                PresenceState     = "Carved",
                PrimarySourceType = "Carved (signature)",
                CarvedImagePath   = imagePath,
                CarvedOffset      = c.Offset,
                CarvedLength      = c.Length,
                ResolutionText    = string.Empty,
                PreferredDateText = string.Empty,
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
