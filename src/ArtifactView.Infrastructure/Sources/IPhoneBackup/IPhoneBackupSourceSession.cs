using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.IPhoneBackup;

public sealed class IPhoneBackupSourceSession(string backupRoot) : ISourceSession
{
    public string SourceId => "iphone-backup:" + backupRoot;

    public async IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(backupRoot, "Manifest.db");
        var records      = ManifestDbReader.ReadMediaFiles(manifestPath);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var physicalPath = ManifestDbReader.PhysicalPath(backupRoot, record.FileId);
            long? size = null;
            try { if (File.Exists(physicalPath)) size = new FileInfo(physicalPath).Length; }
            catch { }

            yield return new SourceItemDescriptor
            {
                ItemId      = physicalPath,
                DisplayName = record.DisplayName,
                LogicalPath = physicalPath,
                Size        = size,
                Extension   = Path.GetExtension(record.RelativePath)
            };

            await Task.Yield();
        }
    }

    public ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        if (!File.Exists(itemId))
            throw new FileNotFoundException($"Backup file not found: {itemId}");

        Stream stream = new FileStream(itemId, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        return ValueTask.FromResult(stream);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
