using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources;

public sealed class ZbThumbnailSourceProvider : ISourceProvider
{
    public string Id          => "core.source.zbthumbnail";
    public string DisplayName => "ZbThumbnail.info (Zoner Photo Studio cache)";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        ISourceSession session = new ZbThumbnailSourceSession(request.Location);
        return ValueTask.FromResult(session);
    }
}
