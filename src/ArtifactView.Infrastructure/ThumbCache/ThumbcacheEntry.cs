namespace ArtifactView.Infrastructure.ThumbCache;

// One cached thumbnail entry from a Windows thumbcache_*.db file.
// These files are located in %LocalAppData%\Microsoft\Windows\Explorer\
// and are the modern replacement for Thumbs.db (used since Windows Vista).
public sealed record ThumbcacheEntry(
    long   EntryOffset,
    ulong  Hash,
    int    DataSize,
    int    HeaderSize,
    string CacheFileName)
{
    // Human-readable size hint for display.
    public string SizeText => DataSize switch
    {
        < 1024         => $"{DataSize} B",
        < 1024 * 1024  => $"{DataSize / 1024.0:F1} KB",
        _              => $"{DataSize / (1024.0 * 1024.0):F1} MB"
    };
}
