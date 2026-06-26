using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.DiskImage;

public sealed class DiskImageSourceProvider : ISourceProvider
{
    // Supported raw disk image extensions.
    public static readonly IReadOnlyList<string> SupportedExtensions =
        [".dd", ".img", ".raw", ".bin", ".iso"];

    public string Id          => "disk-image";
    public string DisplayName => "Disk Image";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        var path = request.Location;
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException($"Disk image not found: {path}");

        return ValueTask.FromResult<ISourceSession>(new DiskImageSourceSession(path));
    }
}
