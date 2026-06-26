using Microsoft.Data.Sqlite;

namespace ArtifactView.Infrastructure.Sources.IPhoneBackup;

// Reads the Files table from an iTunes/Finder iPhone backup Manifest.db.
// Flags: 1 = regular file, 2 = directory, 4 = symlink.
public static class ManifestDbReader
{
    private static readonly HashSet<string> s_mediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".gif", ".bmp", ".tif", ".tiff",
        ".webp", ".avif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".raf",
        ".mp4", ".mov", ".m4v", ".3gp", ".avi", ".mkv"
    };

    // Returns the physical file path within the backup for a given fileID.
    // iOS 9+: <backup_root>/<first2>/<fileID>
    // Pre-iOS 9 backups stored files flat — this method covers both by checking.
    public static string PhysicalPath(string backupRoot, string fileId)
    {
        if (fileId.Length < 2) return Path.Combine(backupRoot, fileId);
        var sub = Path.Combine(backupRoot, fileId[..2], fileId);
        if (File.Exists(sub)) return sub;
        return Path.Combine(backupRoot, fileId);
    }

    // Reads all regular-file media entries from Manifest.db.
    // Returns empty list when db is missing or unreadable.
    public static IReadOnlyList<ManifestRecord> ReadMediaFiles(string manifestDbPath)
    {
        if (!File.Exists(manifestDbPath)) return [];

        var results = new List<ManifestRecord>();
        try
        {
            using var conn = new SqliteConnection($"Data Source={manifestDbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            // flags = 1 → regular file; skip directories and symlinks.
            cmd.CommandText = "SELECT fileID, domain, relativePath FROM Files WHERE flags = 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId       = reader.GetString(0);
                var domain       = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var relativePath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                if (string.IsNullOrEmpty(fileId)) continue;

                var ext = Path.GetExtension(relativePath);
                if (!s_mediaExtensions.Contains(ext)) continue;

                results.Add(new ManifestRecord(fileId, domain, relativePath));
            }
        }
        catch { return []; }

        return results;
    }
}
