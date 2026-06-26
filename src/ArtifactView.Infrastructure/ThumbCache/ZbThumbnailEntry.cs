namespace ArtifactView.Infrastructure.ThumbCache;

// One cached thumbnail from a ZbThumbnail.info file.
// ZbThumbnail.info is a per-folder thumbnail cache created by Zoner Photo Studio
// and some third-party file managers.  Like Thumbs.db, it persists on external
// media and is a forensic source for ghost files.
public sealed record ZbThumbnailEntry(
    string    OriginalFilename,
    int       Width,
    int       Height,
    int       OriginalFileSize,
    DateTime? LastModifiedUtc,
    long      PayloadOffset,
    int       PayloadSize);
