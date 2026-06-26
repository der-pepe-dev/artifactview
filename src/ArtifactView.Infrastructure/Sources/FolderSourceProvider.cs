using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class FolderSourceProvider : ISourceProvider
{
    public string Id          => "core.source.folder";
    public string DisplayName => "Filesystem folder";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        request.Options.TryGetValue("recursive", out var recursiveOpt);
        var recursive = string.Equals(recursiveOpt, "true", StringComparison.OrdinalIgnoreCase);

        ISourceSession session = new FolderSourceSession(request.Location, recursive);
        return ValueTask.FromResult(session);
    }
}
