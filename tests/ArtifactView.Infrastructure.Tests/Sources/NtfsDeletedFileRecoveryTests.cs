using System.IO;
using ArtifactView.Infrastructure.Sources.DiskImage;

namespace ArtifactView.Infrastructure.Tests.Sources;

// Tests the NTFS undelete algorithm against synthetic MFT records. (A real NTFS volume
// can't be formatted on Linux/WSL — DiscUtils' formatter needs Windows SIDs — so the
// DiscUtils integration in ReadDeletedFileBytes isn't unit-tested here; the parsing and
// cluster-reconstruction logic, which is the substance, is.)
public sealed class NtfsDeletedFileRecoveryTests
{
    private static void W16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void W32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
    private static void W64(byte[] b, int o, long v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

    // Builds a 1024-byte MFT record with a single unnamed $DATA attribute.
    private static byte[] MakeRecord(Action<byte[]> writeDataAttr, int firstAttr = 0x38)
    {
        var rec = new byte[1024];
        rec[0] = (byte)'F'; rec[1] = (byte)'I'; rec[2] = (byte)'L'; rec[3] = (byte)'E';
        W16(rec, 0x04, 0x30); // USA offset
        W16(rec, 0x06, 3);    // USA count (USN + 2 sectors)
        W16(rec, 0x14, firstAttr);
        writeDataAttr(rec);
        return rec;
    }

    [Test]
    public async Task ParseDataAttribute_reads_resident_data()
    {
        var content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0x33 };
        var rec = MakeRecord(b =>
        {
            int ao = 0x38, contentOff = 24;
            W32(b, ao + 0, 0x80);                 // type $DATA
            W32(b, ao + 4, (uint)(contentOff + content.Length)); // attr length
            b[ao + 8] = 0;                        // resident
            b[ao + 9] = 0;                        // unnamed
            W32(b, ao + 16, (uint)content.Length); // content length
            W16(b, ao + 20, contentOff);          // content offset
            Array.Copy(content, 0, b, ao + contentOff, content.Length);
            W32(b, ao + contentOff + content.Length, 0xFFFFFFFF); // end marker
        });

        var data = NtfsDeletedFileRecovery.ParseDataAttribute(rec);

        await Assert.That(data).IsNotNull();
        await Assert.That(data!.IsResident).IsTrue();
        await Assert.That(data.ResidentBytes!).IsEquivalentTo(content);
    }

    [Test]
    public async Task Recover_resident_returns_inline_bytes()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var rec = MakeRecord(b =>
        {
            int ao = 0x38, contentOff = 24;
            W32(b, ao + 0, 0x80);
            W32(b, ao + 4, (uint)(contentOff + content.Length));
            b[ao + 8] = 0; b[ao + 9] = 0;
            W32(b, ao + 16, (uint)content.Length);
            W16(b, ao + 20, contentOff);
            Array.Copy(content, 0, b, ao + contentOff, content.Length);
            W32(b, ao + contentOff + content.Length, 0xFFFFFFFF);
        });

        // Resident data needs no volume access.
        var bytes = NtfsDeletedFileRecovery.Recover(Stream.Null, 0, 512, rec, 1 << 20);

        await Assert.That(bytes!).IsEquivalentTo(content);
    }

    [Test]
    public async Task Recover_non_resident_reads_clusters_from_volume()
    {
        const int clusterSize = 512;
        const long lcn = 2;          // data starts at cluster 2
        const int realSize = 600;    // spans 2 clusters, truncated to 600 bytes

        // Synthetic volume with a known pattern at the data run's location.
        var volume = new byte[clusterSize * 8];
        for (int i = 0; i < clusterSize * 2; i++) volume[lcn * clusterSize + i] = (byte)((i * 13 + 5) & 0xFF);

        var rec = MakeRecord(b =>
        {
            int ao = 0x38, runsOff = 64;
            W32(b, ao + 0, 0x80);                 // type $DATA
            W32(b, ao + 4, (uint)(runsOff + 8));  // attr length
            b[ao + 8] = 1;                        // non-resident
            b[ao + 9] = 0;                        // unnamed
            W16(b, ao + 12, 0);                   // flags (not compressed/encrypted)
            W16(b, ao + 32, runsOff);             // data-runs offset
            W64(b, ao + 48, realSize);            // real size
            // One run: length nibble=1, offset nibble=2 -> header 0x21; len=2 clusters; LCN=2.
            int p = ao + runsOff;
            b[p++] = 0x21; b[p++] = 2; W16(b, p, (int)lcn); p += 2;
            b[p] = 0x00;                          // runs terminator
            W32(b, ao + runsOff + 8, 0xFFFFFFFF); // end marker
        });

        var bytes = NtfsDeletedFileRecovery.Recover(new MemoryStream(volume), 0, clusterSize, rec, 1 << 20);

        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes!.Length).IsEqualTo(realSize);
        var expected = volume[(int)(lcn * clusterSize)..(int)(lcn * clusterSize + realSize)];
        await Assert.That(bytes).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ParseDataAttribute_returns_null_for_compressed()
    {
        var rec = MakeRecord(b =>
        {
            int ao = 0x38;
            W32(b, ao + 0, 0x80);
            W32(b, ao + 4, 80);
            b[ao + 8] = 1; b[ao + 9] = 0;
            W16(b, ao + 12, 0x0001); // compressed flag
            W16(b, ao + 32, 64);
            W64(b, ao + 48, 100);
            b[ao + 64] = 0x00;
            W32(b, ao + 72, 0xFFFFFFFF);
        });

        await Assert.That(NtfsDeletedFileRecovery.ParseDataAttribute(rec)).IsNull();
    }

    [Test]
    public async Task ApplyFixup_restores_sector_end_bytes()
    {
        var rec = new byte[1024];
        W16(rec, 0x04, 0x30); // USA offset
        W16(rec, 0x06, 3);    // USN + 2 entries
        // USA replacement values for the two sectors.
        rec[0x30] = 0xAA; rec[0x31] = 0xBB;            // USN (sector-end signature)
        rec[0x32] = 0x11; rec[0x33] = 0x22;            // sector 0 real bytes
        rec[0x34] = 0x33; rec[0x35] = 0x44;            // sector 1 real bytes

        NtfsDeletedFileRecovery.ApplyFixup(rec);

        await Assert.That(rec[510]).IsEqualTo((byte)0x11);
        await Assert.That(rec[511]).IsEqualTo((byte)0x22);
        await Assert.That(rec[1022]).IsEqualTo((byte)0x33);
        await Assert.That(rec[1023]).IsEqualTo((byte)0x44);
    }
}
