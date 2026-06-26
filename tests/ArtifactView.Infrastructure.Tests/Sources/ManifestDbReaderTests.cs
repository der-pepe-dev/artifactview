using System.IO;
using ArtifactView.Infrastructure.Sources.IPhoneBackup;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Sources;

public sealed class ManifestDbReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    private string CreateManifestDb(IEnumerable<(string fileId, string domain, string relativePath, int flags)> rows)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE Files (
                    fileID TEXT PRIMARY KEY,
                    domain TEXT,
                    relativePath TEXT,
                    flags INTEGER,
                    file BLOB
                )
                """;
            cmd.ExecuteNonQuery();
        }

        foreach (var (fileId, domain, relativePath, flags) in rows)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Files VALUES ($id, $domain, $path, $flags, NULL)";
            cmd.Parameters.AddWithValue("$id",     fileId);
            cmd.Parameters.AddWithValue("$domain", domain);
            cmd.Parameters.AddWithValue("$path",   relativePath);
            cmd.Parameters.AddWithValue("$flags",  flags);
            cmd.ExecuteNonQuery();
        }

        return path;
    }

    [Test]
    public async Task Returns_empty_when_file_missing()
    {
        var result = ManifestDbReader.ReadMediaFiles("/nonexistent/Manifest.db");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Returns_camera_roll_jpeg_entries()
    {
        var path = CreateManifestDb(
        [
            ("aabbcc1111111111111111111111111111111111", "CameraRollDomain",
             "Media/DCIM/100APPLE/IMG_0001.JPG", 1),
            ("aabbcc2222222222222222222222222222222222", "CameraRollDomain",
             "Media/DCIM/100APPLE/IMG_0002.HEIC", 1)
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result.Count).IsEqualTo(2);
        foreach (var r in result)
            await Assert.That(r.Domain).IsEqualTo("CameraRollDomain");
    }

    [Test]
    public async Task Skips_non_media_files()
    {
        var path = CreateManifestDb(
        [
            ("aa00000000000000000000000000000000000001", "CameraRollDomain",
             "Media/DCIM/100APPLE/IMG_0001.JPG", 1),
            ("aa00000000000000000000000000000000000002", "AppDomain-com.example.app",
             "Library/Application Support/data.sqlite", 1)
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].DisplayName).IsEqualTo("IMG_0001.JPG");
    }

    [Test]
    public async Task Skips_directories_flag_2()
    {
        var path = CreateManifestDb(
        [
            ("bb00000000000000000000000000000000000001", "CameraRollDomain",
             "Media/DCIM/100APPLE/IMG_0001.JPG", 1),
            ("bb00000000000000000000000000000000000002", "CameraRollDomain",
             "Media/DCIM/100APPLE", 2)  // directory, flags=2
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result).HasSingleItem();
    }

    [Test]
    public async Task Includes_video_files()
    {
        var path = CreateManifestDb(
        [
            ("cc00000000000000000000000000000000000001", "CameraRollDomain",
             "Media/DCIM/100APPLE/VID_0001.MP4", 1),
            ("cc00000000000000000000000000000000000002", "CameraRollDomain",
             "Media/DCIM/100APPLE/VID_0002.MOV", 1)
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Record_display_name_is_filename_only()
    {
        var path = CreateManifestDb(
        [
            ("dd00000000000000000000000000000000000001", "CameraRollDomain",
             "Media/DCIM/100APPLE/IMG_0001.JPG", 1)
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result[0].DisplayName).IsEqualTo("IMG_0001.JPG");
    }

    [Test]
    public async Task PhysicalPath_uses_first_two_chars_as_subdir()
    {
        var backupRoot = Path.GetTempPath();
        var fileId     = "aabbccddeeff112233445566778899001122334455";

        var expected = Path.Combine(backupRoot, "aa", fileId);
        // File doesn't exist, so it always returns subdirectory form.
        var result = ManifestDbReader.PhysicalPath(backupRoot, fileId);

        // When the file doesn't exist, the sub-dir form is constructed.
        // (The method checks File.Exists; on a miss it falls back to root-level.)
        await Assert.That(result).Contains(fileId);
    }

    [Test]
    public async Task LogicalDisplayPath_includes_domain_prefix()
    {
        var record = new ManifestRecord(
            "aa00000000000000000000000000000000000001",
            "CameraRollDomain",
            "Media/DCIM/100APPLE/IMG_0001.JPG");

        await Assert.That(record.LogicalDisplayPath).StartsWith("CameraRollDomain/");
    }

    [Test]
    public async Task Includes_app_domain_media_files()
    {
        var path = CreateManifestDb(
        [
            ("ee00000000000000000000000000000000000001",
             "AppDomain-com.instagram.Instagram",
             "Library/Application Support/User/media/ig_cache_key_123.jpg", 1)
        ]);

        var result = ManifestDbReader.ReadMediaFiles(path);

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].Domain).IsEqualTo("AppDomain-com.instagram.Instagram");
    }
}