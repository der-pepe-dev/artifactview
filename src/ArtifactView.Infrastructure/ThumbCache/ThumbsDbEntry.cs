namespace ArtifactView.Infrastructure.ThumbCache;

// One cached thumbnail from a Thumbs.db OLE Compound Document.
// OriginalFilename comes from the Catalog stream; the image payload
// lives in a separate numbered stream inside the same file.
public sealed record ThumbsDbEntry(
    string    OriginalFilename,
    int       StreamIndex,
    int       Width,
    int       Height,
    int       PayloadSize,
    string    StreamName,
    DateTime? LastModifiedUtc,
    ushort    CatalogVersion);

// Maps catalog version numbers to the Windows edition that wrote the file.
// Forensically useful: tells the examiner which OS originally created
// the Thumbs.db, even if the media has since been used elsewhere.
public static class ThumbsDbVersionMap
{
    public static string ToOsHint(ushort version) => version switch
    {
        5 => "Windows 2000 / XP",
        6 => "Windows Vista",
        7 => "Windows 7",
        _ => $"Unknown (v{version})"
    };
}
