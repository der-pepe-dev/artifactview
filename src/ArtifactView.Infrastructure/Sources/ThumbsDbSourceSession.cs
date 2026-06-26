using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ThumbsDbSourceSession(string dbPath) : ISourceSession
{
    public string SourceId => "thumbsdb:" + dbPath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<ThumbsDbEntry> entries;
        try { entries = ThumbsDbReader.ReadEntries(dbPath); }
        catch { yield break; }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceItemDescriptor
            {
                ItemId      = $"{dbPath}::{entry.StreamName}",
                DisplayName = entry.OriginalFilename,
                LogicalPath = entry.OriginalFilename,
                Size        = entry.PayloadSize,
                Extension   = Path.GetExtension(entry.OriginalFilename)
            };
            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        var streamName = itemId.Contains("::") ? itemId[(itemId.LastIndexOf("::") + 2)..] : itemId;
        var entries = ThumbsDbReader.ReadEntries(dbPath);
        var entry = entries.FirstOrDefault(e => e.StreamName == streamName);
        if (entry is null)
            throw new FileNotFoundException($"Stream '{streamName}' not found in {dbPath}");

        var payload = ThumbsDbReader.ExtractPayload(dbPath, entry)
            ?? throw new InvalidDataException($"Could not extract payload for '{streamName}'");

        return ValueTask.FromResult<Stream>(new MemoryStream(payload));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
