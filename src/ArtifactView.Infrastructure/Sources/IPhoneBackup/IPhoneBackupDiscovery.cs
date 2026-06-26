namespace ArtifactView.Infrastructure.Sources.IPhoneBackup;

// Locates iTunes/Finder iPhone backup directories on the current Windows machine.
public static class IPhoneBackupDiscovery
{
    public sealed record BackupInfo(
        string BackupRoot,
        string DeviceName,
        string Udid,
        DateTime? LastBackupDate
    );

    // Known backup root locations on Windows.
    private static IEnumerable<string> BackupRoots()
    {
        // iTunes (legacy) — %APPDATA%\Apple Computer\MobileSync\Backup
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData, "Apple Computer", "MobileSync", "Backup");

        // Apple Devices app (Windows Store, current) — %USERPROFILE%\Apple\MobileSync\Backup
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, "Apple", "MobileSync", "Backup");
    }

    // Returns all valid backup directories found on this machine.
    public static IReadOnlyList<BackupInfo> DiscoverAll()
    {
        var results = new List<BackupInfo>();

        foreach (var root in BackupRoots())
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    if (!File.Exists(Path.Combine(dir, "Manifest.db"))) continue;

                    var udid       = Path.GetFileName(dir);
                    var deviceName = TryReadDeviceName(dir);
                    var lastDate   = TryReadLastBackupDate(dir);

                    results.Add(new BackupInfo(dir, deviceName ?? udid, udid, lastDate));
                }
            }
            catch { }
        }

        return results;
    }

    private static string? TryReadDeviceName(string backupRoot)
    {
        // Info.plist (XML plist) contains "Device Name" key.
        var infoPath = Path.Combine(backupRoot, "Info.plist");
        if (!File.Exists(infoPath)) return null;
        try
        {
            var text = File.ReadAllText(infoPath);
            // Minimal XML plist key-value extraction — avoids a plist library dependency.
            var key = "<key>Device Name</key>";
            var idx = text.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = text.IndexOf("<string>", idx + key.Length, StringComparison.Ordinal);
            if (start < 0) return null;
            var end = text.IndexOf("</string>", start, StringComparison.Ordinal);
            if (end < 0) return null;
            return text[(start + 8)..end];
        }
        catch { return null; }
    }

    private static DateTime? TryReadLastBackupDate(string backupRoot)
    {
        // Info.plist contains "Last Backup Date" as <date>…</date>.
        var infoPath = Path.Combine(backupRoot, "Info.plist");
        if (!File.Exists(infoPath)) return null;
        try
        {
            var text = File.ReadAllText(infoPath);
            var key  = "<key>Last Backup Date</key>";
            var idx  = text.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;
            var start = text.IndexOf("<date>", idx + key.Length, StringComparison.Ordinal);
            if (start < 0) return null;
            var end = text.IndexOf("</date>", start, StringComparison.Ordinal);
            if (end < 0) return null;
            var raw = text[(start + 6)..end];
            return DateTime.TryParse(raw, out var dt) ? dt.ToUniversalTime() : null;
        }
        catch { return null; }
    }
}
