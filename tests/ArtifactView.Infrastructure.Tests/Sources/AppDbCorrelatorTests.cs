using System.IO;
using ArtifactView.Infrastructure.Sources.AppDb;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Sources;

public sealed class AppDbCorrelatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public AppDbCorrelatorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateWhatsAppDb(IEnumerable<string> filePaths)
    {
        var db = Path.Combine(_tempDir, "msgstore.db");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE message_media (
                    _id INTEGER PRIMARY KEY,
                    file_path TEXT
                )
                """;
            cmd.ExecuteNonQuery();
        }
        foreach (var fp in filePaths)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO message_media (file_path) VALUES ($p)";
            cmd.Parameters.AddWithValue("$p", fp);
            cmd.ExecuteNonQuery();
        }
        return db;
    }

    // ── WhatsApp ──────────────────────────────────────────────────────────

    [Test]
    public async Task WhatsApp_detect_returns_true_when_msgstore_present()
    {
        CreateWhatsAppDb([]);
        var reader = new WhatsAppDbReader();
        await Assert.That(reader.Detect(_tempDir)).IsTrue();
    }

    [Test]
    public async Task WhatsApp_detect_returns_false_when_no_db()
    {
        var reader = new WhatsAppDbReader();
        await Assert.That(reader.Detect(_tempDir)).IsFalse();
    }

    [Test]
    public async Task WhatsApp_correlate_matches_filename()
    {
        CreateWhatsAppDb(["/storage/WhatsApp/Media/WhatsApp Images/IMG_20210101.jpg"]);
        var reader = new WhatsAppDbReader();
        var results = reader.Correlate(_tempDir, ["IMG_20210101.jpg", "other.jpg"]);

        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].MediaFilename).IsEqualTo("IMG_20210101.jpg");
        await Assert.That(results[0].AppName).IsEqualTo("WhatsApp");
        await Assert.That(results[0].Confidence).IsEqualTo(AppDbCorrelationConfidence.High);
    }

    [Test]
    public async Task WhatsApp_correlate_no_match_returns_empty()
    {
        CreateWhatsAppDb(["/storage/WhatsApp/Media/WhatsApp Images/IMG_OTHER.jpg"]);
        var reader   = new WhatsAppDbReader();
        var results  = reader.Correlate(_tempDir, ["UNRELATED.jpg"]);
        await Assert.That(results).IsEmpty();
    }

    // ── AppDbCorrelator ───────────────────────────────────────────────────

    [Test]
    public async Task Correlator_returns_empty_when_no_db_present()
    {
        var correlator = new AppDbCorrelator();
        var results    = correlator.Correlate(_tempDir, ["photo.jpg"]);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Correlator_returns_entry_when_whatsapp_db_found()
    {
        CreateWhatsAppDb(["/storage/WhatsApp/Media/photo.jpg"]);
        var correlator = new AppDbCorrelator();
        var results    = correlator.Correlate(_tempDir, ["photo.jpg"]);

        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].AppName).IsEqualTo("WhatsApp");
    }

    [Test]
    public async Task Correlator_is_case_insensitive_on_filename()
    {
        CreateWhatsAppDb(["/storage/WhatsApp/Media/PHOTO.JPG"]);
        var correlator = new AppDbCorrelator();
        // Match lower-case query against upper-case DB entry.
        var results = correlator.Correlate(_tempDir, ["photo.jpg"]);

        await Assert.That(results).HasSingleItem();
    }

    // ── Signal ────────────────────────────────────────────────────────────

    [Test]
    public async Task Signal_detect_returns_true_when_signal_db_present()
    {
        var db = Path.Combine(_tempDir, "signal.db");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE attachment (file_name TEXT)";
        cmd.ExecuteNonQuery();

        var reader = new SignalDbReader();
        await Assert.That(reader.Detect(_tempDir)).IsTrue();
    }
}