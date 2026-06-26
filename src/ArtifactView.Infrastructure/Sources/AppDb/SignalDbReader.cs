using Microsoft.Data.Sqlite;

namespace ArtifactView.Infrastructure.Sources.AppDb;

// Correlates media files against Signal's signal.db (Android logical extraction).
// Signal stores attachment references in the part / attachment table.
// Common path: /data/data/org.thoughtcrime.securesms/databases/signal.db
public sealed class SignalDbReader : IAppDbReader
{
    private static readonly string[] s_dbNames = ["signal.db", "signal.sqlite"];

    public string AppName => "Signal";

    public bool Detect(string folderPath)
    {
        foreach (var name in s_dbNames)
            if (System.IO.File.Exists(System.IO.Path.Combine(folderPath, name))) return true;
        return false;
    }

    public IReadOnlyList<AppDbCorrelationEntry> Correlate(
        string folderPath,
        IReadOnlyCollection<string> mediaFilenames)
    {
        var dbPath = FindDb(folderPath);
        if (dbPath is null) return [];

        var knownFilenames = QueryAttachmentFilenames(dbPath);
        if (knownFilenames.Count == 0) return [];

        var results = new List<AppDbCorrelationEntry>();
        foreach (var file in mediaFilenames)
        {
            if (knownFilenames.Contains(file))
                results.Add(new AppDbCorrelationEntry(
                    file, AppName,
                    $"File referenced in Signal attachment database ({dbPath}).",
                    AppDbCorrelationConfidence.High));
        }
        return results;
    }

    private static string? FindDb(string folderPath)
    {
        foreach (var name in s_dbNames)
        {
            var p = System.IO.Path.Combine(folderPath, name);
            if (System.IO.File.Exists(p)) return p;
        }
        return null;
    }

    private static HashSet<string> QueryAttachmentFilenames(string dbPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            foreach (var query in new[]
            {
                "SELECT file_name FROM attachment WHERE file_name IS NOT NULL",
                "SELECT _data FROM part WHERE _data IS NOT NULL",
                "SELECT file_name FROM part WHERE file_name IS NOT NULL"
            })
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = query;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var raw  = reader.GetString(0);
                        var name = System.IO.Path.GetFileName(raw);
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                    break;
                }
                catch { }
            }
        }
        catch { }
        return result;
    }
}
