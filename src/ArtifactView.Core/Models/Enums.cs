namespace ArtifactView.Core.Models;

public enum MediaKind
{
    Unknown = 0,
    Image = 1,
    Video = 2
}

public enum PresenceState
{
    Present = 0,
    Ghost = 1,
    Deleted = 2,
    CacheOnly = 3,
    Unknown = 4
}

public enum EmbeddedArtifactType
{
    Unknown = 0,
    ExifThumbnail = 1,
    MotionPhotoVideo = 2,
    DepthMap = 3,
    GainMap = 4,
    SecondaryImage = 5,
    CachePreview = 6,
    UnknownTrailer = 7
}

public enum DecodeStatus
{
    NotAttempted = 0,
    Success = 1,
    Partial = 2,
    Failed = 3
}

public enum ReviewPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum MergeStatus
{
    Resolved = 0,
    Merged = 1,
    Ambiguous = 2,
    Conflicted = 3
}
