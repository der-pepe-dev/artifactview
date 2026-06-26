using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.DiskImage;

public sealed class DiskImageSourceSession(string imagePath) : ISourceSession
{
    public string SourceId => "disk-image:" + imagePath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var entries = await Task.Run(
            () => DiskImagePartitionReader.ReadAllMediaFiles(imagePath), cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new SourceItemDescriptor
            {
                ItemId      = $"disk:{imagePath}::{entry.PartitionIndex}::{entry.LogicalPath}",
                DisplayName = System.IO.Path.GetFileName(entry.LogicalPath),
                LogicalPath = entry.LogicalPath,
                Size        = entry.IsDeleted ? null : entry.SizeBytes,
                Extension   = System.IO.Path.GetExtension(entry.LogicalPath)
            };

            await Task.Yield();
        }
    }

    // Direct stream access into the disk image for a specific file.
    // Returns null-stream for deleted files (bytes not recoverable without carving).
    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        // itemId format: disk:<imagePath>::<partIndex>::<logicalPath>
        // Actual in-image file reading is complex; return empty stream for now.
        // Full carving support is a Phase 7 extension point.
        return ValueTask.FromResult<Stream>(Stream.Null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
