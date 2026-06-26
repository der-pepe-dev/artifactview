using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ThumbcacheSourceSession(string dbPath) : ISourceSession
{
    public string SourceId => "thumbcache:" + dbPath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<ThumbcacheEntry> entries;
        try { entries = ThumbcacheReader.ReadEntries(dbPath); }
        catch { yield break; }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceItemDescriptor
            {
                ItemId      = $"{dbPath}::{entry.Hash:X16}",
                DisplayName = entry.CacheFileName,
                LogicalPath = entry.CacheFileName,
                Size        = entry.DataSize,
                Extension   = Path.GetExtension(entry.CacheFileName)
            };
            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        var hashPart = itemId.Contains("::") ? itemId[(itemId.LastIndexOf("::") + 2)..] : itemId;
        if (!ulong.TryParse(hashPart, System.Globalization.NumberStyles.HexNumber, null, out var hash))
            throw new ArgumentException($"Invalid thumbcache item ID: {itemId}");

        var entries = ThumbcacheReader.ReadEntries(dbPath);
        var entry = entries.FirstOrDefault(e => e.Hash == hash);
        if (entry is null)
            throw new FileNotFoundException($"Hash {hash:X16} not found in {dbPath}");

        var payload = ThumbcacheReader.ExtractPayloadDirect(dbPath, entry.EntryOffset + entry.HeaderSize, entry.DataSize)
            ?? throw new InvalidDataException($"Could not extract payload for hash {hash:X16}");

        return ValueTask.FromResult<Stream>(new MemoryStream(payload));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
