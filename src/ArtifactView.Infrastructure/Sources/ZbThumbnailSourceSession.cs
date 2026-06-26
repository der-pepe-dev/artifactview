using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.ThumbCache;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ZbThumbnailSourceSession(string dbPath) : ISourceSession
{
    public string SourceId => "zbthumbnail:" + dbPath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<ZbThumbnailEntry> entries;
        try { entries = ZbThumbnailReader.ReadEntries(dbPath); }
        catch { yield break; }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceItemDescriptor
            {
                ItemId      = $"{dbPath}::{entry.OriginalFilename}",
                DisplayName = entry.OriginalFilename,
                LogicalPath = entry.OriginalFilename,
                Size        = entry.OriginalFileSize,
                Extension   = Path.GetExtension(entry.OriginalFilename)
            };
            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        var filename = itemId.Contains("::") ? itemId[(itemId.LastIndexOf("::") + 2)..] : itemId;
        var entries = ZbThumbnailReader.ReadEntries(dbPath);
        var entry = entries.FirstOrDefault(e => e.OriginalFilename == filename);
        if (entry is null)
            throw new FileNotFoundException($"'{filename}' not found in {dbPath}");

        var payload = ZbThumbnailReader.ExtractPayload(dbPath, entry)
            ?? throw new InvalidDataException($"Could not extract payload for '{filename}'");

        return ValueTask.FromResult<Stream>(new MemoryStream(payload));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
