using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ThumbcacheSourceProvider : ISourceProvider
{
    public string Id          => "core.source.thumbcache";
    public string DisplayName => "Windows thumbcache_*.db";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        ISourceSession session = new ThumbcacheSourceSession(request.Location);
        return ValueTask.FromResult(session);
    }
}
