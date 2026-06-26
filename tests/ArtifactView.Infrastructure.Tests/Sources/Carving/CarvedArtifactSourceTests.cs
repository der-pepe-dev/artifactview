using ArtifactView.Contracts.Sources;
using ArtifactView.Infrastructure.Sources.Carving;

namespace ArtifactView.Infrastructure.Tests.Sources.Carving;

public sealed class CarvedArtifactSourceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    private static byte[] MakeJpeg() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0xAA, 0xBB,
         0xFF, 0xDA, 0x00, 0x03, 0x01, 0x11, 0x22, 0xFF, 0x00, 0x33, 0xFF, 0xD9];

    private string WriteImage(byte[] data)
    {
        var path = Path.GetTempFileName() + ".raw";
        File.WriteAllBytes(path, data);
        _tempFiles.Add(path);
        return path;
    }

    private static async Task<List<SourceItemDescriptor>> ToList(ISourceSession s)
    {
        var items = new List<SourceItemDescriptor>();
        await foreach (var it in s.EnumerateItemsAsync(CancellationToken.None))
            items.Add(it);
        return items;
    }

    [Test]
    public async Task Session_enumerates_carved_items_and_reads_exact_bytes()
    {
        var jpeg = MakeJpeg();
        var image = new byte[3 + jpeg.Length];
        Array.Copy(jpeg, 0, image, 3, jpeg.Length); // 3 bytes of leading junk
        var path = WriteImage(image);

        var provider = new CarvedArtifactSourceProvider();
        await using var session = await provider.OpenAsync(
            new SourceOpenRequest { Location = path }, CancellationToken.None);

        var items = await ToList(session);

        await Assert.That(items.Count).IsEqualTo(1);
        await Assert.That(items[0].Extension).IsEqualTo(".jpg");
        await Assert.That(items[0].Size).IsEqualTo((long)jpeg.Length);

        await using var stream = await session.OpenReadAsync(items[0].ItemId, CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        await Assert.That(ms.ToArray()).IsEquivalentTo(jpeg);
    }

    [Test]
    public async Task Provider_throws_for_missing_image()
    {
        var provider = new CarvedArtifactSourceProvider();
        await Assert.That(async () => await provider.OpenAsync(
            new SourceOpenRequest { Location = "/no/such/image_99999.raw" }, CancellationToken.None))
            .Throws<FileNotFoundException>();
    }
}
