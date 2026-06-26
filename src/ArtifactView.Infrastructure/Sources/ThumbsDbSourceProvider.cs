using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ThumbsDbSourceProvider : ISourceProvider
{
    public string Id          => "core.source.thumbsdb";
    public string DisplayName => "Thumbs.db (OLE thumbnail cache)";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        ISourceSession session = new ThumbsDbSourceSession(request.Location);
        return ValueTask.FromResult(session);
    }
}
