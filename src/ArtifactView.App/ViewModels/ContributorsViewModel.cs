using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArtifactView.Core.Models;

namespace ArtifactView.App.ViewModels;

// Lists the evidence sources that contributed to the selected media entity.
// Reads only already-enriched properties on MediaEntityRow — no file I/O.
// Rebuilds automatically when enrichment updates row properties.
public sealed class ContributorsViewModel : INotifyPropertyChanged
{
    private MediaEntityRow? _currentRow;

    public ObservableCollection<EvidenceContributor> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    // Synchronous — called on the UI thread from SelectedItem.set.
    public void Load(MediaEntityRow? row)
    {
        if (_currentRow is not null)
            _currentRow.PropertyChanged -= OnRowEnriched;

        _currentRow = row;

        if (_currentRow is not null && !_currentRow.IsDirectory)
            _currentRow.PropertyChanged += OnRowEnriched;

        Rebuild();
    }

    // Fires on the UI thread when the enrichment pass updates a row property
    // (ResolutionText, CameraModel, GpsText, etc.).  Rebuilds so the pane
    // reflects evidence sources discovered after the initial selection click.
    private void OnRowEnriched(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaEntityRow.ResolutionText)
                           or nameof(MediaEntityRow.CameraModel)
                           or nameof(MediaEntityRow.GpsText)
                           or nameof(MediaEntityRow.DetectedFormat))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Entries.Clear();

        var row = _currentRow;
        if (row is null || row.IsDirectory || string.IsNullOrEmpty(row.LogicalPath))
        {
            OnPropertyChanged(nameof(HasEntries));
            return;
        }

        // ── Live file or ghost ───────────────────────────────────────────
        if (row.PresenceState == "Ghost")
        {
            var ghostSource = !string.IsNullOrEmpty(row.ThumbsDbPath) ? "Thumbs.db"
                            : !string.IsNullOrEmpty(row.ZbThumbnailPath) ? "ZbThumbnail.info"
                            : !string.IsNullOrEmpty(row.ThumbcachePath) ? "Thumbcache"
                            : "Cache";
            var ghostDesc = ghostSource switch
            {
                "Thumbcache"       => "Cached thumbnail from Windows thumbcache \u2014 original file not found on disk.",
                "ZbThumbnail.info" => "Cached thumbnail from Zoner Photo Studio cache \u2014 original file not found on disk.",
                _                  => "Cached thumbnail from Windows Explorer \u2014 original file not found on disk."
            };

            var ghostPath = !string.IsNullOrEmpty(row.ThumbsDbPath) ? row.ThumbsDbPath
                          : !string.IsNullOrEmpty(row.ZbThumbnailPath) ? row.ZbThumbnailPath
                          : row.ThumbcachePath;

            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = ghostSource,
                Description = ghostDesc,
                Confidence  = 0.85,
                SourcePath  = ghostPath
            });
        }
        else
        {
            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "Live file",
                Description = !string.IsNullOrEmpty(row.FileSizeText)
                    ? $"{row.FileSizeText} on disk"
                    : "File present on disk",
                Confidence  = 1.0,
                SourcePath  = row.LogicalPath
            });
        }

        // ── EXIF metadata ────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(row.ResolutionText))
        {
            var parts = new List<string>(3) { row.ResolutionText };
            if (!string.IsNullOrEmpty(row.CameraModel))
                parts.Add(row.CameraModel);
            if (!string.IsNullOrEmpty(row.PreferredDateText))
                parts.Add(row.PreferredDateText);

            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "EXIF metadata",
                Description = string.Join("  \u00b7  ", parts),
                Confidence  = 0.95
            });
        }

        // ── GPS coordinates ──────────────────────────────────────────────
        if (!string.IsNullOrEmpty(row.GpsText))
        {
            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "GPS coordinates",
                Description = row.GpsText,
                Confidence  = 0.9
            });
        }

        // ── Thumbs.db cache (for matched live files) ─────────────────────
        if (row.PresenceState != "Ghost" && !string.IsNullOrEmpty(row.ThumbsDbPath))
        {
            var desc = row.ThumbsDbModifiedUtc.HasValue
                ? $"Cached thumbnail dated {row.ThumbsDbModifiedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : "Cached thumbnail present";

            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "Thumbs.db",
                Description = desc,
                Confidence  = 0.85,
                SourcePath  = row.ThumbsDbPath
            });
        }

        // ── Windows thumbcache (for matched live files) ──────────────────
        if (row.PresenceState != "Ghost" && !string.IsNullOrEmpty(row.ThumbcachePath))
        {
            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "Thumbcache",
                Description = $"Windows Explorer thumbnail cache (hash: {row.ThumbcacheHash:X16})",
                Confidence  = 0.80,
                SourcePath  = row.ThumbcachePath
            });
        }

        // ── ZbThumbnail.info cache (for matched live files) ──────────────
        if (row.PresenceState != "Ghost" && !string.IsNullOrEmpty(row.ZbThumbnailPath))
        {
            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "ZbThumbnail.info",
                Description = "Zoner Photo Studio thumbnail cache",
                Confidence  = 0.80,
                SourcePath  = row.ZbThumbnailPath
            });
        }

        // ── Format detection ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(row.DetectedFormat))
        {
            var isMismatch = row.DetectedFormat.Contains('\u26a0');
            Entries.Add(new EvidenceContributor
            {
                Id          = Guid.NewGuid(),
                SourceKind  = "Format detection",
                Description = isMismatch
                    ? $"Detected: {row.DetectedFormat} — extension mismatch"
                    : $"Detected: {row.DetectedFormat}",
                Confidence  = 0.95
            });
        }

        OnPropertyChanged(nameof(HasEntries));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
