using System.IO;
using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.Sources;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Sources;

public sealed class FolderSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FolderSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WriteFile(string name, byte[]? content = null) =>
        File.WriteAllBytes(Path.Combine(_dir, name), content ?? [0xFF, 0xD8, 0xFF]);

    [Test]
    public async Task EnumerateItemsAsync_returns_image_files()
    {
        WriteFile("photo.jpg");
        WriteFile("image.png");
        WriteFile("readme.txt");

        var session = new FolderSourceSession(_dir, recursive: false);
        var items = new List<SourceItemDescriptor>();
        await foreach (var item in session.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);

        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items).Contains(i => i.DisplayName == "photo.jpg");
        await Assert.That(items).Contains(i => i.DisplayName == "image.png");
    }

    [Test]
    public async Task EnumerateItemsAsync_skips_non_image_extensions()
    {
        WriteFile("doc.pdf");
        WriteFile("video.mp4");
        WriteFile("photo.jpg");

        var session = new FolderSourceSession(_dir, recursive: false);
        var items = new List<SourceItemDescriptor>();
        await foreach (var item in session.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);

        await Assert.That(items).HasSingleItem();
        await Assert.That(items[0].DisplayName).IsEqualTo("photo.jpg");
    }

    [Test]
    public async Task EnumerateItemsAsync_recursive_finds_subdirectory_files()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        WriteFile("top.jpg");
        File.WriteAllBytes(Path.Combine(sub, "nested.png"), [0x89, 0x50, 0x4E, 0x47]);

        var session = new FolderSourceSession(_dir, recursive: true);
        var items = new List<SourceItemDescriptor>();
        await foreach (var item in session.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);

        await Assert.That(items.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EnumerateItemsAsync_non_recursive_ignores_subdirectories()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        WriteFile("top.jpg");
        File.WriteAllBytes(Path.Combine(sub, "nested.jpg"), [0xFF, 0xD8, 0xFF]);

        var session = new FolderSourceSession(_dir, recursive: false);
        var items = new List<SourceItemDescriptor>();
        await foreach (var item in session.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);

        await Assert.That(items).HasSingleItem();
        await Assert.That(items[0].DisplayName).IsEqualTo("top.jpg");
    }

    [Test]
    public async Task EnumerateItemsAsync_yields_empty_for_missing_folder()
    {
        var session = new FolderSourceSession("/nonexistent/path/xyz", recursive: false);
        var items = new List<SourceItemDescriptor>();
        await foreach (var item in session.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);

        await Assert.That(items).IsEmpty();
    }

    [Test]
    public async Task OpenReadAsync_returns_stream_for_existing_file()
    {
        WriteFile("photo.jpg");
        var session = new FolderSourceSession(_dir, recursive: false);
        var path = Path.Combine(_dir, "photo.jpg");

        await using var stream = await session.OpenReadAsync(path, CancellationToken.None);

        Assert.NotNull(stream);
        await Assert.That(stream.Length > 0).IsTrue();
    }

    [Test]
    public async Task OpenReadAsync_throws_for_missing_file()
    {
        var session = new FolderSourceSession(_dir, recursive: false);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => session.OpenReadAsync("/nonexistent/photo.jpg", CancellationToken.None).AsTask());
    }

    [Test]
    public async Task SourceId_includes_folder_path()
    {
        var session = new FolderSourceSession(_dir, recursive: false);
        await Assert.That(session.SourceId).Contains(_dir);
    }

    [Test]
    public async Task Provider_id_is_correct()
    {
        var provider = new FolderSourceProvider();
        await Assert.That(provider.Id).IsEqualTo("core.source.folder");
    }

    [Test]
    public async Task Provider_opens_non_recursive_by_default()
    {
        var provider = new FolderSourceProvider();
        var request = new SourceOpenRequest { Location = _dir };
        await using var session = await provider.OpenAsync(request, CancellationToken.None);
        Assert.NotNull(session);
    }

    [Test]
    public async Task Provider_opens_recursive_when_option_set()
    {
        var provider = new FolderSourceProvider();
        var request = new SourceOpenRequest
        {
            Location = _dir,
            Options  = { ["recursive"] = "true" }
        };
        await using var session = await provider.OpenAsync(request, CancellationToken.None);

        var folderSession = await Assert.That(session).IsTypeOf<FolderSourceSession>();
        // Verify recursive works by placing a file in a subdirectory.
        var sub = Path.Combine(_dir, "sub2");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "x.jpg"), [0xFF, 0xD8, 0xFF]);

        var items = new List<SourceItemDescriptor>();
        await foreach (var item in folderSession.EnumerateItemsAsync(CancellationToken.None))
            items.Add(item);
        await Assert.That(items).HasSingleItem();
    }
}