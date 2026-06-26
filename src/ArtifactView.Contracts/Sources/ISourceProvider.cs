namespace ArtifactView.Contracts.Sources;

public interface ISourceProvider
{
    string Id { get; }
    string DisplayName { get; }
    ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken);
}
