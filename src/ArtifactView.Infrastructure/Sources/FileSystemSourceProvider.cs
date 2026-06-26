using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class FileSystemSourceProvider : ISourceProvider
{
    public string Id => "filesystem";
    public string DisplayName => "Local filesystem";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        ISourceSession session = new FileSystemSourceSession(request.Location);
        return ValueTask.FromResult(session);
    }
}
