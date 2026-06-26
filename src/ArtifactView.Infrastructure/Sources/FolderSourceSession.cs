using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class FolderSourceSession(string folderPath, bool recursive) : ISourceSession
{
    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif",
        ".png", ".gif", ".bmp", ".dib",
        ".tif", ".tiff",
        ".webp", ".heic", ".heif", ".avif",
        ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".orf", ".rw2", ".pef", ".dng", ".raf", ".raw"
    };

    public string SourceId => "folder:" + folderPath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IEnumerable<string> files;
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            files = Directory.EnumerateFiles(folderPath, "*", option);
        }
        catch { yield break; }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(file);
            if (!s_imageExtensions.Contains(ext))
            {
                await Task.Yield();
                continue;
            }

            long? size = null;
            try { size = new FileInfo(file).Length; } catch { }

            yield return new SourceItemDescriptor
            {
                ItemId      = file,
                DisplayName = Path.GetFileName(file),
                LogicalPath = file,
                Size        = size,
                Extension   = ext
            };

            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        if (!File.Exists(itemId))
            throw new FileNotFoundException($"File not found: {itemId}");

        Stream stream = new FileStream(itemId, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        return ValueTask.FromResult(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
