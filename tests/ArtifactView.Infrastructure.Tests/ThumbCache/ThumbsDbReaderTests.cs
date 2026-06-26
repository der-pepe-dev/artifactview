using System.IO;
using System.Text;
using OpenMcdf;
using ArtifactView.Infrastructure.ThumbCache;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.ThumbCache;

public sealed class ThumbsDbReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    // Minimal valid JPEG: SOI + APP0 stub + EOI (6 bytes).
    private static byte[] TinyJpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9];

    // Builds a thumbnail stream: 16-byte header + JPEG payload.
    // Header: uint32 headerSize=16, uint32 id, uint32 width, uint32 height
    private static byte[] BuildThumbStream(int id, int w, int h, byte[] jpeg)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(16);    // header size
        bw.Write(id);    // catalog id
        bw.Write(w);     // width
        bw.Write(h);     // height
        bw.Write(jpeg);
        bw.Flush();
        return ms.ToArray();
    }

    // Builds a Catalog stream with the given id→filename mappings.
    // Catalog header: uint16 headerSize=16, uint16 version=5,
    //                 uint32 count, uint32 thumbW, uint32 thumbH
    // Each entry:     uint32 entrySize, uint32 id,
    //                 uint64 filetime=0, wchar[] filename + null
    private static byte[] BuildCatalog(params (int id, string name)[] items)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);

        // Header
        bw.Write((ushort)16);         // headerSize
        bw.Write((ushort)5);          // version
        bw.Write(items.Length);       // entry count
        bw.Write(96);                 // thumb width
        bw.Write(96);                 // thumb height

        foreach (var (id, name) in items)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name);
            var entrySize = 4 + 4 + 8 + nameBytes.Length + 2; // +2 for null terminator
            bw.Write(entrySize);
            bw.Write(id);
            bw.Write(0L);             // FILETIME = 0
            bw.Write(nameBytes);
            bw.Write((ushort)0);      // null terminator
        }

        bw.Flush();
        return ms.ToArray();
    }

    // Creates a Thumbs.db OLE Compound Document with the given catalog
    // and thumbnail streams, saved to a temp file.
    private string CreateThumbsDb(
        (int id, string name)[] catalogEntries,
        (int id, int w, int h)[] thumbnails)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);

        var jpeg = TinyJpeg();

        using (var cf = RootStorage.Create(path))
        {
            // Catalog stream
            using (var cat = cf.CreateStream("Catalog"))
            {
                var catData = BuildCatalog(catalogEntries);
                cat.Write(catData, 0, catData.Length);
            }

            // Numbered thumbnail streams
            foreach (var (id, w, h) in thumbnails)
            {
                using var s = cf.CreateStream(id.ToString());
                var streamData = BuildThumbStream(id, w, h, jpeg);
                s.Write(streamData, 0, streamData.Length);
            }
        }

        return path;
    }

    // ── tests ────────────────────────────────────────────────────────────

    [Test]
    public async Task ReadEntries_ValidThumbsDb_ReturnsEntries()
    {
        var path = CreateThumbsDb(
            [(1, "photo.jpg"), (2, "sunset.png")],
            [(1, 96, 96), (2, 96, 72)]);

        var entries = ThumbsDbReader.ReadEntries(path);

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries).Contains(e => e.OriginalFilename == "photo.jpg" && e.StreamIndex == 1);
        await Assert.That(entries).Contains(e => e.OriginalFilename == "sunset.png" && e.StreamIndex == 2);
    }

    [Test]
    public async Task ReadEntries_ReturnsCatalogDimensions()
    {
        var path = CreateThumbsDb(
            [(1, "img.jpg")],
            [(1, 120, 80)]);

        var entries = ThumbsDbReader.ReadEntries(path);

        await Assert.That(entries).HasSingleItem();
        // Dimensions come from the Catalog header (96×96 in BuildCatalog),
        // not from the individual stream header.
        await Assert.That(entries[0].Width).IsEqualTo(96);
        await Assert.That(entries[0].Height).IsEqualTo(96);
    }

    [Test]
    public async Task ReadEntries_MissingCatalog_UsesStreamIndexAsName()
    {
        // Create a Thumbs.db with thumbnail streams but NO Catalog.
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        var jpeg = TinyJpeg();
        using (var cf = RootStorage.Create(path))
        {
            using var s = cf.CreateStream("1");
            var data = BuildThumbStream(1, 64, 64, jpeg);
            s.Write(data, 0, data.Length);
        }

        var entries = ThumbsDbReader.ReadEntries(path);

        await Assert.That(entries).HasSingleItem();
        await Assert.That(entries[0].OriginalFilename).IsEqualTo("#1");
    }

    [Test]
    public async Task ExtractPayload_ReturnsJpegBytes()
    {
        var path = CreateThumbsDb(
            [(1, "pic.jpg")],
            [(1, 96, 96)]);

        var entries = ThumbsDbReader.ReadEntries(path);
        var payload = ThumbsDbReader.ExtractPayload(path, entries[0]);

        Assert.NotNull(payload);
        // Starts with JPEG SOI
        await Assert.That(payload.Length >= 3).IsTrue();
        await Assert.That(payload[0]).IsEqualTo((byte)0xFF);
        await Assert.That(payload[1]).IsEqualTo((byte)0xD8);
        await Assert.That(payload[2]).IsEqualTo((byte)0xFF);
    }

    [Test]
    public void ExtractPayload_MissingStream_ReturnsNull()
    {
        var path = CreateThumbsDb([(1, "a.jpg")], [(1, 96, 96)]);
        var fake = new ThumbsDbEntry("x.jpg", 99, 0, 0, 0, "99", null, 0);
        Assert.Null(ThumbsDbReader.ExtractPayload(path, fake));
    }

    [Test]
    public async Task ReadEntries_NonExistentFile_ReturnsEmpty()
    {
        await Assert.That(ThumbsDbReader.ReadEntries(@"C:\no_such_file_thumbs.db")).IsEmpty();
    }

    [Test]
    public async Task ReadEntries_NotAnOleFile_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);
        await Assert.That(ThumbsDbReader.ReadEntries(path)).IsEmpty();
    }

    // ── real Thumbs.db fixture ───────────────────────────────────────────

    // Uses the real Thumbs.db committed to tests/ — skip if the fixture is missing.
    private static string? FindRealThumbsDb()
    {
        // Walk up from the test output dir to find the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Thumbs.db");
            if (File.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir.FullName, "Thumbs.db");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Test]
    public async Task ReadEntries_RealThumbsDb_ReturnsNonEmptyEntries()
    {
        var path = FindRealThumbsDb();
        Skip.When(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);

        await Assert.That(entries).IsNotEmpty();
        foreach (var e in entries)
        {
            await Assert.That(string.IsNullOrEmpty(e.OriginalFilename)).IsFalse();
            await Assert.That(e.PayloadSize > 0).IsTrue().Because($"Entry {e.StreamIndex} has no payload");
        }
    }

    [Test]
    public async Task ReadEntries_RealThumbsDb_HasLastModifiedAndVersion()
    {
        var path = FindRealThumbsDb();
        Skip.When(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);
        await Assert.That(entries).IsNotEmpty();

        // All entries should carry the catalog version.
        await Assert.That(entries).All(e => e.CatalogVersion > 0);

        // At least one entry should have a valid LastModifiedUtc.
        await Assert.That(entries).Contains(e =>
            e.LastModifiedUtc.HasValue && e.LastModifiedUtc.Value.Year > 2000);
    }

    [Test]
    public async Task ExtractPayload_RealThumbsDb_ReturnsValidJpeg()
    {
        var path = FindRealThumbsDb();
        Skip.When(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);
        Skip.When(entries.Count == 0, "No entries in Thumbs.db fixture.");

        var payload = ThumbsDbReader.ExtractPayload(path, entries[0]);

        Assert.NotNull(payload);
        await Assert.That(payload.Length > 3).IsTrue();
        // Must start with JPEG SOI (FF D8 FF)
        await Assert.That(payload[0]).IsEqualTo((byte)0xFF);
        await Assert.That(payload[1]).IsEqualTo((byte)0xD8);
        await Assert.That(payload[2]).IsEqualTo((byte)0xFF);
    }
}