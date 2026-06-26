using Microsoft.Data.Sqlite;

namespace ArtifactView.Infrastructure.Sources.AppDb;

// Correlates media files against Telegram's cache4.db (Android logical extraction).
// Common path: Telegram/cache4.db or /data/data/org.telegram.messenger/files/cache4.db
public sealed class TelegramDbReader : IAppDbReader
{
    private static readonly string[] s_dbNames = ["cache4.db", "cache4_v2.db"];

    public string AppName => "Telegram";

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

        var knownFilenames = QueryDocumentFilenames(dbPath);
        if (knownFilenames.Count == 0) return [];

        var results = new List<AppDbCorrelationEntry>();
        foreach (var file in mediaFilenames)
        {
            if (knownFilenames.Contains(file))
                results.Add(new AppDbCorrelationEntry(
                    file, AppName,
                    $"File referenced in Telegram database ({dbPath}).",
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

    private static HashSet<string> QueryDocumentFilenames(string dbPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            foreach (var query in new[]
            {
                "SELECT file_name FROM documents WHERE file_name IS NOT NULL",
                "SELECT path FROM media WHERE path IS NOT NULL"
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
