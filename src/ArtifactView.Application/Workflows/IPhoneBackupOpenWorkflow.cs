using ArtifactView.Contracts.Sources;
using ArtifactView.Core.Models;
using ArtifactView.Infrastructure.Sources.IPhoneBackup;
using Microsoft.Extensions.Logging;

namespace ArtifactView.Application.Workflows;

public sealed class IPhoneBackupOpenWorkflow(ILogger<IPhoneBackupOpenWorkflow> logger)
{
    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".gif", ".bmp", ".tif", ".tiff",
        ".webp", ".avif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf"
    };

    private static readonly HashSet<string> s_videoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".3gp", ".avi", ".mkv"
    };

    public async IAsyncEnumerable<MediaEntityRow> OpenBackupAsync(
        string backupRoot,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Opening iPhone backup: {BackupRoot}", backupRoot);

        var manifestPath = Path.Combine(backupRoot, "Manifest.db");
        var records      = ManifestDbReader.ReadMediaFiles(manifestPath);

        // Group by domain for ordered output — Camera Roll first, then apps.
        var ordered = records
            .OrderBy(r => DomainOrder(r.Domain))
            .ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (var record in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var physicalPath = ManifestDbReader.PhysicalPath(backupRoot, record.FileId);
            var exists       = File.Exists(physicalPath);
            var ext          = Path.GetExtension(record.RelativePath);

            long   sizeBytes = 0;
            string sizeText  = string.Empty;
            string dateText  = string.Empty;
            if (exists)
            {
                try
                {
                    var fi    = new FileInfo(physicalPath);
                    sizeBytes = fi.Length;
                    sizeText  = FormatSize(fi.Length);
                    dateText  = fi.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch { }
            }

            yield return new MediaEntityRow
            {
                DisplayName       = record.DisplayName,
                LogicalPath       = exists ? physicalPath : string.Empty,
                IsDirectory       = false,
                SortOrder         = 2,
                ItemIcon          = exists ? IconForExtension(ext) : "\U0001F47B",
                FileSizeText      = sizeText,
                FileSizeBytes     = sizeBytes,
                PresenceState     = exists ? "Backup" : "Ghost",
                PrimarySourceType = SourceLabel(record.Domain),
                ResolutionText    = string.Empty,
                PreferredDateText = dateText,
                CameraModel       = string.Empty,
                FindingsText      = string.Empty
            };

            await Task.Yield();
        }
    }

    // Camera Roll first (0), then all other domains alphabetically.
    private static int DomainOrder(string domain) =>
        domain.StartsWith("CameraRollDomain", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static string SourceLabel(string domain)
    {
        if (domain.StartsWith("CameraRollDomain", StringComparison.OrdinalIgnoreCase))
            return "iPhone Camera Roll";
        if (domain.StartsWith("MediaDomain", StringComparison.OrdinalIgnoreCase))
            return "iPhone Media";
        if (domain.StartsWith("AppDomain", StringComparison.OrdinalIgnoreCase))
        {
            // Extract app bundle id from "AppDomain-com.example.app" or
            // "AppDomainGroup-group.com.apple.photos.local"
            var idx = domain.IndexOf('-');
            return idx >= 0 ? $"iPhone App ({domain[(idx + 1)..]})" : "iPhone App";
        }
        return $"iPhone Backup ({domain})";
    }

    private static string IconForExtension(string ext) =>
        s_imageExtensions.Contains(ext) ? "\U0001F5BC" :
        s_videoExtensions.Contains(ext) ? "\U0001F3AC" : "\U0001F4C4";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
