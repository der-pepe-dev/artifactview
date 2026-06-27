using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArtifactView.Core.Models;

// Grid row viewmodel. Properties are mutable to support incremental enrichment
// as metadata, findings, and integrity results arrive from background jobs.
public sealed class MediaEntityRow : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _logicalPath = string.Empty;
    private bool   _isDirectory;
    private string _itemIcon = string.Empty;
    private string _fileSizeText = string.Empty;
    private string _presenceState = string.Empty;

    // Stable group key used as the primary sort description so the view always shows
    // ".." first, then directories, then media files — regardless of column sort.
    // 0 = ".."  |  1 = directory  |  2 = media file
    public int SortOrder { get; init; }
    private string _primarySourceType = string.Empty;
    private string _resolutionText = string.Empty;
    private string _preferredDateText = string.Empty;
    private string _cameraModel = string.Empty;
    private string _gpsText     = string.Empty;
    private string _findingsText = string.Empty;
    private string _detectedFormat = string.Empty;

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    // Full path to the source item — used by the viewer to open the file.
    public string LogicalPath
    {
        get => _logicalPath;
        set { _logicalPath = value; OnPropertyChanged(); }
    }

    public bool IsDirectory
    {
        get => _isDirectory;
        set { _isDirectory = value; OnPropertyChanged(); }
    }

    // Single-character icon shown in the grid icon column (emoji, rendered via font fallback).
    public string ItemIcon
    {
        get => _itemIcon;
        set { _itemIcon = value; OnPropertyChanged(); }
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        set { _fileSizeText = value; OnPropertyChanged(); }
    }

    // Raw byte count for numeric sort; FileSizeText is the human-readable display.
    public long FileSizeBytes { get; init; }

    // For entries that have a matching Thumbs.db cache record — applies to BOTH
    // ghost entries (file deleted) and live files (enrichment source).
    // Empty when no Thumbs.db match exists.
    public string    ThumbsDbPath        { get; init; } = string.Empty;
    public string    ThumbsDbStreamName  { get; init; } = string.Empty;
    public DateTime? ThumbsDbModifiedUtc { get; init; }

    // For entries matched against a Windows thumbcache_*.db (Vista+).
    // ThumbcachePath is the path to the thumbcache_*.db file that contains
    // the entry; ThumbcacheHash is the content hash used to locate it.
    // PayloadOffset and DataSize allow direct-seek extraction without
    // re-reading all entries from the (potentially 100 MB+) cache file.
    public string ThumbcachePath          { get; init; } = string.Empty;
    public ulong  ThumbcacheHash          { get; init; }
    public long   ThumbcachePayloadOffset { get; init; }
    public int    ThumbcacheDataSize      { get; init; }

    // For entries matched against a ZbThumbnail.info file (Zoner Photo Studio
    // per-folder thumbnail cache).  Like Thumbs.db, these persist on external
    // media after file deletion and are a forensic source for ghost files.
    public string ZbThumbnailPath          { get; init; } = string.Empty;
    public long   ZbThumbnailPayloadOffset { get; init; }
    public int    ZbThumbnailDataSize      { get; init; }

    // For artifacts recovered by signature carving from a raw image (no filesystem entry).
    // The bytes are the range [CarvedOffset, CarvedOffset + CarvedLength) within
    // CarvedImagePath — the viewer reads them directly, like the cache-extraction sources.
    public string CarvedImagePath { get; init; } = string.Empty;
    public long   CarvedOffset    { get; init; }
    public long   CarvedLength    { get; init; }

    // For LIVE files inside a raw disk image (no host-filesystem path). The viewer reads
    // the file content out of the image via DiscUtils using these coordinates.
    public string DiskImagePath          { get; init; } = string.Empty;
    public int    DiskImagePartitionIndex { get; init; }
    public string DiskImageInternalPath { get; init; } = string.Empty;
    public string DiskImageFilesystem   { get; init; } = string.Empty;

    // For DELETED NTFS files: the $MFT record number used to recover bytes on demand.
    // -1 when not a recoverable deleted entry.
    public long DeletedMftRecordNumber { get; init; } = -1;

    // For DELETED FAT files: the recorded start cluster (contiguous best-effort recovery).
    // -1 when not applicable. FileSizeBytes carries the recorded size.
    public long DeletedFatStartCluster { get; init; } = -1;

    public string PresenceState
    {
        get => _presenceState;
        set { _presenceState = value; OnPropertyChanged(); }
    }

    public string PrimarySourceType
    {
        get => _primarySourceType;
        set { _primarySourceType = value; OnPropertyChanged(); }
    }

    public string ResolutionText
    {
        get => _resolutionText;
        set { _resolutionText = value; OnPropertyChanged(); }
    }

    public string PreferredDateText
    {
        get => _preferredDateText;
        set { _preferredDateText = value; OnPropertyChanged(); }
    }

    public string CameraModel
    {
        get => _cameraModel;
        set { _cameraModel = value; OnPropertyChanged(); }
    }

    public string GpsText
    {
        get => _gpsText;
        set { _gpsText = value; OnPropertyChanged(); }
    }

    public string FindingsText
    {
        get => _findingsText;
        set { _findingsText = value; OnPropertyChanged(); }
    }

    // Magic-byte-detected format (e.g. "JPEG", "PNG ⚠").  Populated during
    // enrichment; the warning suffix appears when format ≠ extension.
    public string DetectedFormat
    {
        get => _detectedFormat;
        set { _detectedFormat = value; OnPropertyChanged(); }
    }

    // Date group key used for timeline grouping (yyyy-MM-dd).
    // Populated during enrichment alongside PreferredDateText.
    private string _dateGroup = string.Empty;
    public string DateGroup
    {
        get => _dateGroup;
        set { _dateGroup = value; OnPropertyChanged(); }
    }

    // Burst and session cluster IDs — assigned after enrichment when all
    // capture timestamps are known.  0 = unclustered (no timestamp or not yet assigned).
    // Files with the same non-zero BurstId were shot within 5 s of each other.
    // Files with the same non-zero SessionId were shot within 30 min of each other.
    private int _burstId;
    public int BurstId
    {
        get => _burstId;
        set { _burstId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBurst)); }
    }

    private int _sessionId;
    public int SessionId
    {
        get => _sessionId;
        set { _sessionId = value; OnPropertyChanged(); }
    }

    public bool IsBurst => _burstId > 0;

    // dHash perceptual hash — populated during the near-duplicate detection pass.
    // 0 = not yet computed or computation failed.
    private ulong _perceptualHash;
    public ulong PerceptualHashValue
    {
        get => _perceptualHash;
        set { _perceptualHash = value; OnPropertyChanged(); }
    }

    // SHA-256 hex string for the file payload — populated during enrichment.
    // Empty string means not yet computed or file unreadable.
    private string _sha256Hash = string.Empty;
    public string Sha256Hash
    {
        get => _sha256Hash;
        set { _sha256Hash = value; OnPropertyChanged(); }
    }

    // Number of other files in the current session with the same SHA-256 hash.
    // Zero means no duplicates found in this folder.
    private int _duplicateCount;
    public int DuplicateCount
    {
        get => _duplicateCount;
        set { _duplicateCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDuplicate)); }
    }

    public bool IsDuplicate => _duplicateCount > 0;

    // Findings pre-computed by background enrichment.  When non-null, the
    // detail-panel FindingsViewModel uses these instead of re-analyzing.
    // Pixel-level comparisons (thumbnail vs main, Thumbs.db visual) are
    // deferred to on-select analysis and appended on top.
    private List<Finding>? _cachedFindings;
    public List<Finding>? CachedFindings
    {
        get => _cachedFindings;
        set { _cachedFindings = value; OnPropertyChanged(); }
    }

    // Short display badge for the top signature match (e.g. "iPhone", "Lightroom").
    // Empty string when no match found.
    private string _workflowBadge = string.Empty;
    public string WorkflowBadge
    {
        get => _workflowBadge;
        set { _workflowBadge = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
