using Microsoft.Data.Sqlite;

namespace ArtifactView.Infrastructure.Sources.AppDb;

// Correlates media files against WhatsApp's msgstore.db (Android backup or PC extraction).
// WhatsApp stores sent/received media references in message_media table.
// Common extraction paths:
//   Android: /sdcard/WhatsApp/Databases/msgstore.db
//   Backup:  WhatsApp/Databases/msgstore.db (inside backup root)
public sealed class WhatsAppDbReader : IAppDbReader
{
    private static readonly string[] s_dbNames = ["msgstore.db", "msgstore.db.crypt15"];

    public string AppName => "WhatsApp";

    public bool Detect(string folderPath)
    {
        foreach (var name in s_dbNames)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(folderPath, name))) return true;
            // Check one level up (media folder alongside Databases folder).
            var parent = System.IO.Path.GetDirectoryName(folderPath);
            if (parent is not null &&
                System.IO.File.Exists(System.IO.Path.Combine(parent, "Databases", name)))
                return true;
        }
        return false;
    }

    public IReadOnlyList<AppDbCorrelationEntry> Correlate(
        string folderPath,
        IReadOnlyCollection<string> mediaFilenames)
    {
        var dbPath = FindDb(folderPath);
        if (dbPath is null) return [];

        var knownFilenames = QueryMediaFilenames(dbPath);
        if (knownFilenames.Count == 0) return [];

        var results = new List<AppDbCorrelationEntry>();
        foreach (var file in mediaFilenames)
        {
            if (knownFilenames.Contains(file))
                results.Add(new AppDbCorrelationEntry(
                    file, AppName,
                    $"File referenced in WhatsApp message database ({dbPath}).",
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
        var parent = System.IO.Path.GetDirectoryName(folderPath);
        if (parent is not null)
        {
            foreach (var name in s_dbNames)
            {
                var p = System.IO.Path.Combine(parent, "Databases", name);
                if (System.IO.File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static HashSet<string> QueryMediaFilenames(string dbPath)
    {
        // Only attempt unencrypted .db files.
        if (!dbPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return [];
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            // WhatsApp schema varies by version; try known column names.
            foreach (var query in new[]
            {
                "SELECT file_path FROM message_media WHERE file_path IS NOT NULL",
                "SELECT local_path FROM message_media WHERE local_path IS NOT NULL",
                "SELECT media_name FROM message_media WHERE media_name IS NOT NULL"
            })
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = query;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var raw = reader.GetString(0);
                        var name = System.IO.Path.GetFileName(raw);
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                    break; // stop on first successful query
                }
                catch { }
            }
        }
        catch { }
        return result;
    }
}
