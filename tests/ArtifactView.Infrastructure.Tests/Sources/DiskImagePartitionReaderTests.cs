using System.IO;
using DiscUtils;
using DiscUtils.Fat;
using ArtifactView.Infrastructure.Sources.DiskImage;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Sources;

public sealed class DiskImagePartitionReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private string SaveTempImage(byte[] data)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, data);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateFatImage(Action<FatFileSystem> populate)
    {
        // 1.44 MB floppy – no MBR partition table, triggers the no-partition-table fallback.
        using var ms = new MemoryStream(1_474_560);
        using (var fat = FatFileSystem.FormatFloppy(ms, FloppyDiskType.HighDensity, "TEST"))
            populate(fat);
        return SaveTempImage(ms.ToArray());
    }

    private static void Write(FatFileSystem fat, string path, byte[] content)
    {
        // DiscUtils FAT uses '\' as separator; Path.GetDirectoryName on Linux won't split on '\'.
        var sep = path.LastIndexOf('\\');
        if (sep > 0)
        {
            var dir = path[..sep];
            if (!fat.DirectoryExists(dir))
                fat.CreateDirectory(dir);
        }
        using var s = fat.OpenFile(path, FileMode.Create, FileAccess.Write);
        s.Write(content);
    }

    // FAT paths use '\' as separator; on Linux Path.GetFileName doesn't split on '\'.
    private static string FatFilename(string logicalPath) =>
        logicalPath.Split(['\\', '/']).Last(s => !string.IsNullOrEmpty(s));

    // ── negative cases ───────────────────────────────────────────────────────

    [Test]
    public async Task ReadAllMediaFiles_on_nonexistent_path_returns_empty()
    {
        var results = DiskImagePartitionReader.ReadAllMediaFiles("/no/such/file_99999.dd");
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task ReadAllMediaFiles_on_random_bytes_returns_empty()
    {
        var data = new byte[4096];
        new Random(42).NextBytes(data);
        var path = SaveTempImage(data);
        await Assert.That(DiskImagePartitionReader.ReadAllMediaFiles(path)).IsEmpty();
    }

    // ── FAT partition ────────────────────────────────────────────────────────

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_returns_media_file()
    {
        var path = CreateFatImage(fat =>
        {
            Write(fat, "photo.jpg", [0xFF, 0xD8, 0xFF, 0xE0]);
            Write(fat, "readme.txt", [0x48, 0x65, 0x6C, 0x6C, 0x6F]);
        });

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        // FAT uppercases 8.3 filenames; use case-insensitive comparison throughout.
        await Assert.That(results).HasSingleItem();
        var entry = results[0];
        await Assert.That(FatFilename(entry.LogicalPath)).IsEqualTo("photo.jpg").IgnoringCase();
        await Assert.That(entry.Filesystem).IsEqualTo("FAT");
        await Assert.That(entry.PartitionIndex).IsEqualTo(0);
        await Assert.That(entry.IsDeleted).IsFalse();
    }

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_excludes_non_media_extensions()
    {
        var path = CreateFatImage(fat =>
        {
            Write(fat, "photo.jpg", [0xFF, 0xD8, 0xFF]);
            Write(fat, "video.mp4", [0x00, 0x00, 0x00, 0x18]);
            Write(fat, "document.pdf", [0x25, 0x50, 0x44, 0x46]);
            Write(fat, "log.txt", []);
        });

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results).Contains(r => FatFilename(r.LogicalPath).Equals("photo.jpg", StringComparison.OrdinalIgnoreCase));
        await Assert.That(results).Contains(r => FatFilename(r.LogicalPath).Equals("video.mp4", StringComparison.OrdinalIgnoreCase));
        await Assert.That(results).DoesNotContain(r => FatFilename(r.LogicalPath).Equals("document.pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_recurses_into_subdirectories()
    {
        var path = CreateFatImage(fat =>
        {
            Write(fat, @"DCIM\100APPLE\IMG_0001.jpg", [0xFF, 0xD8, 0xFF]);
            Write(fat, @"DCIM\100APPLE\VID_0001.mov", [0x00, 0x00, 0x00, 0x14]);
        });

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        await Assert.That(results.Count).IsEqualTo(2);
        foreach (var r in results)
            await Assert.That(r.Filesystem).IsEqualTo("FAT");
    }

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_all_supported_extensions_detected()
    {
        // FAT12 (floppy) only supports 3-char extensions; test the 3-char subset.
        // 4-char extensions (.jpeg, .heic, .heif, .tiff, .webp, .avif) are covered via NTFS.
        var extensions = new[]
        {
            ".jpg", ".png", ".gif", ".bmp", ".tif", ".dng", ".cr2", ".cr3",
            ".nef", ".arw", ".raf",
            ".mp4", ".mov", ".m4v", ".3gp", ".avi", ".mkv"
        };

        var path = CreateFatImage(fat =>
        {
            for (int i = 0; i < extensions.Length; i++)
                Write(fat, $"f{i:D2}{extensions[i]}", [0x00]);
        });

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);
        await Assert.That(results.Count).IsEqualTo(extensions.Length);
    }

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_size_bytes_matches()
    {
        var content = new byte[1234];
        var path = CreateFatImage(fat => Write(fat, "img.png", content));

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        await Assert.That(results).HasSingleItem();
        await Assert.That(results[0].SizeBytes).IsEqualTo(1234L);
    }

    [Test]
    public async Task ReadAllMediaFiles_raw_fat_modified_time_populated()
    {
        var path = CreateFatImage(fat => Write(fat, "photo.jpg", [0xFF, 0xD8]));

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        await Assert.That(results).HasSingleItem();
        Assert.NotNull(results[0].ModifiedUtc);
    }

    // ── IsDeleted flag ────────────────────────────────────────────────────────

    [Test]
    public async Task ReadAllMediaFiles_live_fat_entries_have_IsDeleted_false()
    {
        var path = CreateFatImage(fat => Write(fat, "photo.jpg", [0xFF, 0xD8]));

        var results = DiskImagePartitionReader.ReadAllMediaFiles(path);

        await Assert.That(results).All(r => !(r.IsDeleted));
    }

    // ── ReadFileBytes (live file content) ──────────────────────────────────────

    [Test]
    public async Task ReadFileBytes_returns_live_fat_file_content()
    {
        var content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x12, 0x34, 0x56, 0x78, 0xFF, 0xD9 };
        var path = CreateFatImage(fat => Write(fat, "photo.jpg", content));

        // Use the path exactly as enumeration reports it (FAT 8.3 casing).
        var entry = DiskImagePartitionReader.ReadAllMediaFiles(path).Single(e => !e.IsDeleted);

        var bytes = DiskImagePartitionReader.ReadFileBytes(
            path, entry.PartitionIndex, entry.LogicalPath, entry.Filesystem);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!).IsEquivalentTo(content);
    }

    [Test]
    public async Task ReadFileBytes_returns_null_for_missing_file()
    {
        var path = CreateFatImage(fat => Write(fat, "photo.jpg", [0xFF, 0xD8]));

        var bytes = DiskImagePartitionReader.ReadFileBytes(path, 0, @"\NOPE.JPG", "FAT");

        await Assert.That(bytes).IsNull();
    }
}