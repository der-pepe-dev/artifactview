using ArtifactView.Infrastructure.Sources.Android;
using Xunit;

namespace ArtifactView.Infrastructure.Tests.Sources;

public sealed class AndroidDcimThumbnailScannerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public AndroidDcimThumbnailScannerTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string CreateDir(string name)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TouchFile(string dir, string name)
    {
        File.WriteAllBytes(Path.Combine(dir, name), [0xFF, 0xD8, 0xFF, 0xE0]); // minimal JPEG header
    }

    [Fact]
    public void FindThumbnailsDir_finds_dotThumbnails()
    {
        CreateDir(".thumbnails");
        var result = AndroidDcimThumbnailScanner.FindThumbnailsDir(_tempRoot);
        Assert.NotNull(result);
        Assert.Contains(".thumbnails", result);
    }

    [Fact]
    public void FindThumbnailsDir_finds_Thumbnails_case_variant()
    {
        CreateDir("Thumbnails");
        var result = AndroidDcimThumbnailScanner.FindThumbnailsDir(_tempRoot);
        Assert.NotNull(result);
    }

    [Fact]
    public void FindThumbnailsDir_returns_null_when_absent()
    {
        var result = AndroidDcimThumbnailScanner.FindThumbnailsDir(_tempRoot);
        Assert.Null(result);
    }

    [Fact]
    public void Scan_returns_empty_for_missing_directory()
    {
        var result = AndroidDcimThumbnailScanner.Scan(Path.Combine(_tempRoot, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_returns_entry_for_each_jpg()
    {
        var thumbDir = CreateDir(".thumbnails");
        TouchFile(thumbDir, "100000000001.jpg");
        TouchFile(thumbDir, "100000000002.jpg");

        var result = AndroidDcimThumbnailScanner.Scan(thumbDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Scan_sets_thumbnail_path_to_physical_file()
    {
        var thumbDir = CreateDir(".thumbnails");
        TouchFile(thumbDir, "100000000001.jpg");

        var result = AndroidDcimThumbnailScanner.Scan(thumbDir);

        Assert.Equal(Path.Combine(thumbDir, "100000000001.jpg"), result[0].ThumbnailPath);
    }

    [Fact]
    public void Numeric_only_stem_produces_null_original_filename()
    {
        var thumbDir = CreateDir(".thumbnails");
        // Purely numeric = MediaStore ID, original filename unknowable.
        TouchFile(thumbDir, "1609459200000.jpg");

        var result = AndroidDcimThumbnailScanner.Scan(thumbDir);

        Assert.Single(result);
        Assert.Null(result[0].OriginalFilename);
    }

    [Fact]
    public void Non_numeric_stem_produces_inferred_filename()
    {
        var thumbDir = CreateDir(".thumbnails");
        // Non-numeric stem may be the original filename without extension.
        TouchFile(thumbDir, "IMG_20210101_120000.jpg");

        var result = AndroidDcimThumbnailScanner.Scan(thumbDir);

        Assert.Single(result);
        Assert.NotNull(result[0].OriginalFilename);
        Assert.Contains("IMG_20210101_120000", result[0].OriginalFilename);
    }
}
