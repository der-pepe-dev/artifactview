namespace ArtifactView.Infrastructure.Sources.Android;

// A thumbnail found in an Android DCIM/.thumbnails/ folder.
public sealed record AndroidThumbnailEntry(
    string ThumbnailPath,
    // Original filename if extractable from EXIF or naming convention; null otherwise.
    string? OriginalFilename,
    DateTime? ThumbnailDateUtc,
    int Width,
    int Height
);
