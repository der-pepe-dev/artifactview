namespace ArtifactView.Contracts.Sources;

public interface ISourceSession : IAsyncDisposable
{
    string SourceId { get; }
    IAsyncEnumerable<SourceItemDescriptor> EnumerateItemsAsync(CancellationToken cancellationToken);
    ValueTask<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken);
}
