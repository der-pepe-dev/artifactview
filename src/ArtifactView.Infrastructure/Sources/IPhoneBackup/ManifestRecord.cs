namespace ArtifactView.Infrastructure.Sources.IPhoneBackup;

// A single file entry from the Manifest.db Files table.
public sealed record ManifestRecord(
    string FileId,
    string Domain,
    string RelativePath
)
{
    // Human-readable label: domain prefix + filename.
    public string DisplayName => Path.GetFileName(RelativePath);

    // Logical path shown in the grid — preserves the device's original path.
    public string LogicalDisplayPath =>
        string.IsNullOrEmpty(Domain) ? RelativePath : $"{Domain}/{RelativePath}";
}
