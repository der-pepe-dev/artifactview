using System.IO;
using ArtifactView.Infrastructure.Sources.DiskImage;

namespace ArtifactView.Infrastructure.Tests.Sources;

// DiscUtils can't create exFAT, so these tests build a minimal exFAT volume by hand —
// which also exercises every field the parser reads.
public sealed class ExFatScannerTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { }
    }

    private static void W16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void W32(byte[] b, int o, long v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
    private static void W64(byte[] b, int o, long v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

    // Builds a tiny exFAT image (512B sectors, 1 sector/cluster) with a single DELETED file
    // "photo.jpg" whose data is `content` stored contiguously at cluster 3.
    private string BuildExFatImageWithDeletedFile(byte[] content)
    {
        const int sector = 512;
        var img = new byte[64 * sector]; // 32 KiB

        // Boot sector
        System.Text.Encoding.ASCII.GetBytes("EXFAT   ").CopyTo(img, 3);
        W32(img, 0x50, 8);   // FatOffset (sectors)
        W32(img, 0x54, 1);   // FatLength (sectors)
        W32(img, 0x58, 16);  // ClusterHeapOffset (sectors)
        W32(img, 0x5C, 16);  // ClusterCount
        W32(img, 0x60, 2);   // FirstClusterOfRootDirectory
        img[0x6C] = 9;       // BytesPerSectorShift -> 512
        img[0x6D] = 0;       // SectorsPerClusterShift -> 1

        // FAT (sector 8): mark the root cluster (2) as end-of-chain.
        W32(img, 8 * sector + 2 * 4, 0xFFFFFFFF);

        // Root directory (cluster 2 -> sector 16).
        int root = 16 * sector;
        // File directory entry (deleted: 0x85 -> 0x05).
        img[root + 0] = 0x05;
        img[root + 1] = 2;          // SecondaryCount: Stream + 1 Name
        W16(img, root + 4, 0x0020); // FileAttributes (archive)
        // Stream-Extension entry (deleted: 0xC0 -> 0x40) at +32.
        int stream = root + 32;
        img[stream + 0] = 0x40;
        img[stream + 1] = 0x03;     // AllocationPossible | NoFatChain
        img[stream + 3] = 9;        // NameLength ("photo.jpg")
        W32(img, stream + 20, 3);   // FirstCluster
        W64(img, stream + 24, content.Length); // DataLength
        // File-Name entry (deleted: 0xC1 -> 0x41) at +64.
        int nameEntry = root + 64;
        img[nameEntry + 0] = 0x41;
        const string name = "photo.jpg";
        for (int i = 0; i < name.Length; i++) W16(img, nameEntry + 2 + i * 2, name[i]);

        // File data (cluster 3 -> sector 17).
        content.CopyTo(img, 17 * sector);

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, img);
        _tempFiles.Add(path);
        return path;
    }

    private static byte[] Pattern(int n)
    {
        var b = new byte[n];
        for (int i = 0; i < n; i++) b[i] = (byte)((i * 17 + 9) & 0xFF);
        return b;
    }

    [Test]
    public async Task Enumerates_and_recovers_deleted_exfat_file()
    {
        var content = Pattern(300);
        var path = BuildExFatImageWithDeletedFile(content);

        var deleted = DiskImagePartitionReader.ReadAllMediaFiles(path)
            .Single(e => e.IsDeleted && e.Filesystem == "exFAT");

        await Assert.That(deleted.LogicalPath).EndsWith("photo.jpg");
        await Assert.That(deleted.FatStartCluster).IsEqualTo(3L);
        await Assert.That(deleted.SizeBytes).IsEqualTo((long)content.Length);

        var bytes = DiskImagePartitionReader.ReadDeletedExFatFileBytes(
            path, deleted.PartitionIndex, deleted.FatStartCluster, deleted.SizeBytes);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!).IsEquivalentTo(content);
    }

    [Test]
    public async Task ReadDeletedExFatFileBytes_returns_null_for_bad_cluster()
    {
        var path = BuildExFatImageWithDeletedFile(Pattern(64));
        var bytes = DiskImagePartitionReader.ReadDeletedExFatFileBytes(path, 0, 0, 64);
        await Assert.That(bytes).IsNull();
    }
}
