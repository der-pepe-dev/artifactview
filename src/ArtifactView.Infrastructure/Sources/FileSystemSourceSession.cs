using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class FileSystemSourceSession(string rootPath) : ISourceSession
{
    public string SourceId => "filesystem:" + rootPath;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        // Directories first, case-insensitive alphabetical order
        foreach (var dir in Directory.EnumerateDirectories(rootPath)
                     .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(dir);
            yield return new SourceItemDescriptor
            {
                ItemId      = dir,
                DisplayName = info.Name,
                LogicalPath = dir,
                IsDirectory = true
            };
            await Task.Yield();
        }

        // Files alphabetically
        foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            yield return new SourceItemDescriptor
            {
                ItemId      = file,
                DisplayName = info.Name,
                LogicalPath = file,
                Size        = info.Length,
                Extension   = info.Extension
            };
            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        Stream stream = File.OpenRead(itemId);
        return ValueTask.FromResult(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

