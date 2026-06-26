using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.Carving;

/// <summary>
/// Source that recovers files from a raw image by signature carving — no filesystem
/// metadata required. Complements <c>DiskImageSourceProvider</c> (which reads the live
/// filesystem) by recovering content from unallocated/orphaned regions.
/// </summary>
public sealed class CarvedArtifactSourceProvider : ISourceProvider
{
    public static readonly IReadOnlyList<string> SupportedExtensions =
        [".dd", ".img", ".raw", ".bin", ".iso"];

    public string Id          => "carved-artifact";
    public string DisplayName => "Carved Artifacts";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        var path = request.Location;
        if (!File.Exists(path))
            throw new FileNotFoundException($"Image not found: {path}");

        return ValueTask.FromResult<ISourceSession>(new CarvedArtifactSourceSession(path));
    }
}
