using ArtifactView.Contracts.Sources;

namespace ArtifactView.Infrastructure.Sources.IPhoneBackup;

public sealed class IPhoneBackupSourceProvider : ISourceProvider
{
    public string Id          => "iphone-backup";
    public string DisplayName => "iPhone Backup";

    public ValueTask<ISourceSession> OpenAsync(SourceOpenRequest request, CancellationToken cancellationToken)
    {
        var backupRoot = request.Location;
        if (!Directory.Exists(backupRoot))
            throw new DirectoryNotFoundException($"Backup directory not found: {backupRoot}");
        if (!File.Exists(Path.Combine(backupRoot, "Manifest.db")))
            throw new InvalidOperationException($"No Manifest.db found in: {backupRoot}");

        return ValueTask.FromResult<ISourceSession>(new IPhoneBackupSourceSession(backupRoot));
    }
}
