namespace ArtifactView.Infrastructure.Sources.AppDb;

// Reads a specific app's database and returns correlation entries for media files
// found in the scanned folder.
public interface IAppDbReader
{
    string AppName { get; }

    // Returns true when this reader recognises a database in the given folder tree.
    bool Detect(string folderPath);

    // Correlates media filenames against the app DB. Returns one entry per match.
    IReadOnlyList<AppDbCorrelationEntry> Correlate(
        string folderPath,
        IReadOnlyCollection<string> mediaFilenames);
}
