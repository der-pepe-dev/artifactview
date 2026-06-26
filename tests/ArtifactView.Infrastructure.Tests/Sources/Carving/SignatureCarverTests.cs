using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Sources.Carving;

namespace ArtifactView.Infrastructure.Tests.Sources.Carving;

public sealed class SignatureCarverTests
{
    // Minimal valid-enough JPEG: SOI + APP0 + SOS + entropy (with FF00 stuffing) + EOI.
    private static byte[] MakeJpeg() =>
    [
        0xFF, 0xD8,                         // SOI
        0xFF, 0xE0, 0x00, 0x04, 0xAA, 0xBB, // APP0, len=4 (2 payload bytes)
        0xFF, 0xDA, 0x00, 0x03, 0x01,       // SOS, len=3 (1 payload byte)
        0x11, 0x22, 0xFF, 0x00, 0x33,       // entropy data incl. FF00 stuffing
        0xFF, 0xD9                          // EOI
    ];

    // JPEG whose APP1 segment embeds a thumbnail with its own FF D8 ... FF D9.
    // A naive "first EOI" carver would stop at the thumbnail's EOI; segment-walking
    // must skip the APP1 payload and find the real (outer) EOI.
    private static byte[] MakeJpegWithEmbeddedThumb() =>
    [
        0xFF, 0xD8,                         // SOI
        0xFF, 0xE1, 0x00, 0x06, 0xFF, 0xD8, 0xFF, 0xD9, // APP1 len=6, payload = fake thumb SOI/EOI
        0xFF, 0xDA, 0x00, 0x02,             // SOS, len=2 (0 payload bytes)
        0x55,                               // entropy
        0xFF, 0xD9                          // real (outer) EOI
    ];

    // Minimal PNG: signature + IHDR(13) + IEND(0). CRCs are not verified by the carver.
    private static byte[] MakePng()
    {
        var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]); // signature
        ms.Write([0x00, 0x00, 0x00, 0x0D]);                          // IHDR length = 13
        ms.Write([0x49, 0x48, 0x44, 0x52]);                          // "IHDR"
        ms.Write(new byte[13]);                                       // IHDR data
        ms.Write([0x01, 0x02, 0x03, 0x04]);                          // CRC (ignored)
        ms.Write([0x00, 0x00, 0x00, 0x00]);                          // IEND length = 0
        ms.Write([0x49, 0x45, 0x4E, 0x44]);                          // "IEND"
        ms.Write([0xAE, 0x42, 0x60, 0x82]);                          // CRC (ignored)
        return ms.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }

    [Test]
    public async Task Carves_jpeg_and_png_among_junk()
    {
        var jpeg = MakeJpeg();
        var png  = MakePng();
        var data = Concat([0x00, 0x01, 0x02], jpeg, [0xDE, 0xAD, 0xBE, 0xEF, 0x00], png, [0xFF, 0xFE]);

        var found = SignatureCarver.Carve(data);

        await Assert.That(found.Count).IsEqualTo(2);

        await Assert.That(found[0].Format).IsEqualTo(FormatId.Jpeg);
        await Assert.That(found[0].Offset).IsEqualTo(3L);
        await Assert.That(found[0].Length).IsEqualTo((long)jpeg.Length);
        await Assert.That(data[(int)found[0].Offset..(int)(found[0].Offset + found[0].Length)]).IsEquivalentTo(jpeg);

        await Assert.That(found[1].Format).IsEqualTo(FormatId.Png);
        await Assert.That(found[1].Offset).IsEqualTo((long)(3 + jpeg.Length + 5));
        await Assert.That(found[1].Length).IsEqualTo((long)png.Length);
        await Assert.That(data[(int)found[1].Offset..(int)(found[1].Offset + found[1].Length)]).IsEquivalentTo(png);
    }

    [Test]
    public async Task Carves_back_to_back_artifacts()
    {
        var jpeg = MakeJpeg();
        var png  = MakePng();
        var data = Concat(jpeg, png);

        var found = SignatureCarver.Carve(data);

        await Assert.That(found.Count).IsEqualTo(2);
        await Assert.That(found[0].Offset).IsEqualTo(0L);
        await Assert.That(found[1].Offset).IsEqualTo((long)jpeg.Length);
    }

    [Test]
    public async Task Jpeg_carve_spans_past_embedded_thumbnail_eoi()
    {
        var jpeg = MakeJpegWithEmbeddedThumb();
        var found = SignatureCarver.Carve(jpeg);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Format).IsEqualTo(FormatId.Jpeg);
        // Real EOI is the last 2 bytes — length must equal the whole buffer, not the
        // shorter offset where the embedded thumbnail's EOI sits.
        await Assert.That(found[0].Length).IsEqualTo((long)jpeg.Length);
    }

    // A segment (here a DHT) appears AFTER the first SOS and its payload contains the
    // bytes FF D9. A naive scanner treats the payload as entropy and stops at that FF D9,
    // truncating the carve; the segment must be skipped via its length to reach the real EOI.
    private static byte[] MakeJpegWithSegmentAfterScan() =>
    [
        0xFF, 0xD8,                                     // SOI
        0xFF, 0xDA, 0x00, 0x02,                         // SOS #1 (no payload)
        0x11,                                           // entropy
        0xFF, 0xC4, 0x00, 0x05, 0xFF, 0xD9, 0xAA,       // DHT len=5, payload contains FF D9
        0xFF, 0xDA, 0x00, 0x02,                         // SOS #2 (progressive)
        0x22,                                           // entropy
        0xFF, 0xD9                                      // real EOI
    ];

    [Test]
    public async Task Jpeg_carve_skips_segment_payload_after_scan()
    {
        var jpeg = MakeJpegWithSegmentAfterScan();
        var found = SignatureCarver.Carve(jpeg);

        await Assert.That(found.Count).IsEqualTo(1);
        await Assert.That(found[0].Length).IsEqualTo((long)jpeg.Length);
    }

    [Test]
    public async Task Returns_empty_for_no_signatures()
    {
        var found = SignatureCarver.Carve([0x00, 0x11, 0x22, 0x33, 0x44]);
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task Ignores_truncated_jpeg_without_eoi()
    {
        // SOI + SOS but no EOI -> not a complete artifact.
        var data = Concat([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02], [0x11, 0x22, 0x33]);
        var found = SignatureCarver.Carve(data);
        await Assert.That(found).IsEmpty();
    }

    [Test]
    public async Task CarveFile_reads_and_carves_a_file()
    {
        var jpeg = MakeJpeg();
        var data = Concat([0x00, 0x00], jpeg);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, data);
            var found = SignatureCarver.CarveFile(path);
            await Assert.That(found.Count).IsEqualTo(1);
            await Assert.That(found[0].Offset).IsEqualTo(2L);
            await Assert.That(found[0].Length).IsEqualTo((long)jpeg.Length);
        }
        finally { File.Delete(path); }
    }
}
