using System.Text;
using OpenMcdf;
using Xunit;
using ArtifactView.Infrastructure.ThumbCache;

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

    [Fact]
    public void ReadEntries_ValidThumbsDb_ReturnsEntries()
    {
        var path = CreateThumbsDb(
            [(1, "photo.jpg"), (2, "sunset.png")],
            [(1, 96, 96), (2, 96, 72)]);

        var entries = ThumbsDbReader.ReadEntries(path);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.OriginalFilename == "photo.jpg" && e.StreamIndex == 1);
        Assert.Contains(entries, e => e.OriginalFilename == "sunset.png" && e.StreamIndex == 2);
    }

    [Fact]
    public void ReadEntries_ReturnsCatalogDimensions()
    {
        var path = CreateThumbsDb(
            [(1, "img.jpg")],
            [(1, 120, 80)]);

        var entries = ThumbsDbReader.ReadEntries(path);

        Assert.Single(entries);
        // Dimensions come from the Catalog header (96×96 in BuildCatalog),
        // not from the individual stream header.
        Assert.Equal(96, entries[0].Width);
        Assert.Equal(96, entries[0].Height);
    }

    [Fact]
    public void ReadEntries_MissingCatalog_UsesStreamIndexAsName()
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

        Assert.Single(entries);
        Assert.Equal("#1", entries[0].OriginalFilename);
    }

    [Fact]
    public void ExtractPayload_ReturnsJpegBytes()
    {
        var path = CreateThumbsDb(
            [(1, "pic.jpg")],
            [(1, 96, 96)]);

        var entries = ThumbsDbReader.ReadEntries(path);
        var payload = ThumbsDbReader.ExtractPayload(path, entries[0]);

        Assert.NotNull(payload);
        // Starts with JPEG SOI
        Assert.True(payload.Length >= 3);
        Assert.Equal(0xFF, payload[0]);
        Assert.Equal(0xD8, payload[1]);
        Assert.Equal(0xFF, payload[2]);
    }

    [Fact]
    public void ExtractPayload_MissingStream_ReturnsNull()
    {
        var path = CreateThumbsDb([(1, "a.jpg")], [(1, 96, 96)]);
        var fake = new ThumbsDbEntry("x.jpg", 99, 0, 0, 0, "99", null, 0);
        Assert.Null(ThumbsDbReader.ExtractPayload(path, fake));
    }

    [Fact]
    public void ReadEntries_NonExistentFile_ReturnsEmpty()
    {
        Assert.Empty(ThumbsDbReader.ReadEntries(@"C:\no_such_file_thumbs.db"));
    }

    [Fact]
    public void ReadEntries_NotAnOleFile_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);
        Assert.Empty(ThumbsDbReader.ReadEntries(path));
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

    [SkippableFact]
    public void ReadEntries_RealThumbsDb_ReturnsNonEmptyEntries()
    {
        var path = FindRealThumbsDb();
        Skip.If(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);

        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.OriginalFilename));
            Assert.True(e.PayloadSize > 0, $"Entry {e.StreamIndex} has no payload");
        });
    }

    [SkippableFact]
    public void ReadEntries_RealThumbsDb_HasLastModifiedAndVersion()
    {
        var path = FindRealThumbsDb();
        Skip.If(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);
        Assert.NotEmpty(entries);

        // All entries should carry the catalog version.
        Assert.All(entries, e => Assert.True(e.CatalogVersion > 0,
            $"Entry {e.StreamIndex} has no catalog version"));

        // At least one entry should have a valid LastModifiedUtc.
        Assert.Contains(entries, e =>
            e.LastModifiedUtc.HasValue && e.LastModifiedUtc.Value.Year > 2000);
    }

    [SkippableFact]
    public void ExtractPayload_RealThumbsDb_ReturnsValidJpeg()
    {
        var path = FindRealThumbsDb();
        Skip.If(path is null, "Real Thumbs.db fixture not available in this environment.");

        var entries = ThumbsDbReader.ReadEntries(path);
        Skip.If(entries.Count == 0, "No entries in Thumbs.db fixture.");

        var payload = ThumbsDbReader.ExtractPayload(path, entries[0]);

        Assert.NotNull(payload);
        Assert.True(payload.Length > 3);
        // Must start with JPEG SOI (FF D8 FF)
        Assert.Equal(0xFF, payload[0]);
        Assert.Equal(0xD8, payload[1]);
        Assert.Equal(0xFF, payload[2]);
    }
}
