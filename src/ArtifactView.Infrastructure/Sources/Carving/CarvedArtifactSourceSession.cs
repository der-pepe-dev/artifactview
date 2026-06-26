using System.Globalization;
using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.Carving;

/// <summary>
/// Carves an image file in memory and exposes each recovered artifact as a source item.
/// Unlike <c>DiskImageSourceSession</c>, <see cref="OpenReadAsync"/> returns the actual
/// recovered bytes (the carved byte range) rather than a null stream.
/// </summary>
public sealed class CarvedArtifactSourceSession(string imagePath) : ISourceSession
{
    public string SourceId => "carved-artifact:" + imagePath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var artifacts = await Task.Run(() => SignatureCarver.CarveFile(imagePath), cancellationToken);

        foreach (var a in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new SourceItemDescriptor
            {
                // itemId carries offset+length so OpenReadAsync needs no re-carve.
                ItemId      = $"carved:{imagePath}::{a.Offset}::{a.Length}::{a.Extension}",
                DisplayName = $"carved_{a.Offset:x}{a.Extension}",
                LogicalPath = $"carved/{a.Offset:x}{a.Extension}",
                Size        = a.Length,
                Extension   = a.Extension
            };

            await Task.Yield();
        }
    }

    public async ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        if (!TryParse(itemId, out var offset, out var length))
            return Stream.Null;

        var buffer = new byte[length];
        await using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(offset, SeekOrigin.Begin);
            await fs.ReadExactlyAsync(buffer, cancellationToken);
        }
        return new MemoryStream(buffer, writable: false);
    }

    // itemId format: carved:<path>::<offset>::<length>::<ext>
    private static bool TryParse(string itemId, out long offset, out int length)
    {
        offset = 0; length = 0;
        var parts = itemId.Split("::");
        return parts.Length >= 3
            && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out length)
            && offset >= 0 && length >= 0;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
