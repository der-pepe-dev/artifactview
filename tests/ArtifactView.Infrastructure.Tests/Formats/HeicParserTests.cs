using ArtifactView.Infrastructure.Analysis;
using ArtifactView.Infrastructure.Formats.Heic;
using System.Threading.Tasks;

namespace ArtifactView.Infrastructure.Tests.Formats;

public sealed class HeicParserTests
{
    // ── Binary builder helpers ───────────────────────────────────────────────
    private static byte[] Be2(ushort v) => [(byte)(v >> 8), (byte)v];
    private static byte[] Be4(uint v)   => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] MakeBox(string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type[..Math.Min(4, type.Length)].PadRight(4));
        uint size = (uint)(8 + data.Length);
        return [..Be4(size), ..typeBytes, ..data];
    }

    // FullBox prepends [version:1][flags:3] before the payload data.
    private static byte[] MakeFullBox(string type, byte version, byte[] data)
        => MakeBox(type, [version, 0, 0, 0, ..data]);

    // infe version 2: item_id(2) + protection_index(2) + item_type(4) + item_name(\0)
    private static byte[] MakeInfeV2(ushort itemId, string itemType, bool hidden = false)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(itemType.PadRight(4)[..4]);
        byte[] data = [..Be2(itemId), ..Be2(0), ..typeBytes, 0];
        return MakeBox("infe", [hidden ? (byte)1 : (byte)2, 0, 0, (hidden ? (byte)1 : (byte)0), ..data]);
    }

    // iinf v0: entry_count(2) + infe boxes
    private static byte[] MakeIinf(params byte[][] infes)
    {
        var count = Be2((ushort)infes.Length);
        return MakeFullBox("iinf", 0, [..count, ..infes.SelectMany(x => x)]);
    }

    // SingleItemTypeReferenceBox (plain Box, NOT FullBox):
    //   from_id(2) + ref_count(2) + to_id[](2) each, for iref v0
    private static byte[] MakeRefChild(string refType, ushort fromId, params ushort[] toIds)
    {
        var data = new List<byte>();
        data.AddRange(Be2(fromId));
        data.AddRange(Be2((ushort)toIds.Length));
        foreach (var id in toIds) data.AddRange(Be2(id));
        return MakeBox(refType, [..data]);
    }

    // iref v0: FullBox wrapper around ref children
    private static byte[] MakeIref(params byte[][] children)
        => MakeFullBox("iref", 0, children.SelectMany(x => x).ToArray());

    // pitm v0: item_id(2)
    private static byte[] MakePitm(ushort itemId)
        => MakeFullBox("pitm", 0, Be2(itemId));

    // meta FullBox containing sub-boxes
    private static byte[] MakeMeta(params byte[][] children)
        => MakeFullBox("meta", 0, children.SelectMany(x => x).ToArray());

    private static MemoryStream StreamOf(byte[] data) => new(data);

    // ── Parser tests ─────────────────────────────────────────────────────────

    [Test]
    public async Task Returns_invalid_for_empty_stream()
    {
        var result = HeicParser.Parse(new MemoryStream());
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Returns_invalid_when_no_meta_box()
    {
        // A stream with a ftyp box but no meta box.
        var ftyp = MakeBox("ftyp", [0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0]);
        var result = HeicParser.Parse(StreamOf(ftyp));
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Reads_primary_item_id_from_pitm()
    {
        var meta   = MakeMeta(MakePitm(42));
        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.PrimaryItemId).IsEqualTo(42u);
    }

    [Test]
    public async Task Returns_zero_primary_item_when_pitm_absent()
    {
        var meta   = MakeMeta(MakeIinf());
        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.PrimaryItemId).IsEqualTo(0u);
    }

    [Test]
    public async Task Enumerates_items_from_iinf()
    {
        var infe1 = MakeInfeV2(1, "hvc1");
        var infe2 = MakeInfeV2(2, "Exif");
        var meta  = MakeMeta(MakePitm(1), MakeIinf(infe1, infe2));

        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Items.Count).IsEqualTo(2);
        await Assert.That(result.Items[0].ItemType).IsEqualTo("hvc1");
        await Assert.That(result.Items[1].ItemType).IsEqualTo("Exif");
        await Assert.That(result.Items[0].ItemId).IsEqualTo(1u);
        await Assert.That(result.Items[1].ItemId).IsEqualTo(2u);
    }

    [Test]
    public async Task Returns_empty_items_when_no_iinf()
    {
        var meta   = MakeMeta(MakePitm(1));
        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Items).IsEmpty();
    }

    [Test]
    public async Task Detects_auxiliary_ref()
    {
        // Item 3 is auxiliary to item 1.
        var auxl = MakeRefChild("auxl", fromId: 3, toIds: 1);
        var meta = MakeMeta(
            MakePitm(1),
            MakeIinf(MakeInfeV2(1, "hvc1"), MakeInfeV2(3, "hvc1")),
            MakeIref(auxl));

        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        var auxRefs = result.AuxiliaryRefs.ToList();
        await Assert.That(auxRefs).HasSingleItem();
        await Assert.That(auxRefs[0].FromItemId).IsEqualTo(3u);
        await Assert.That(auxRefs[0].ToItemIds).Contains(1u);
    }

    [Test]
    public async Task Detects_thumbnail_ref()
    {
        var thmb = MakeRefChild("thmb", fromId: 2, toIds: 1);
        var meta = MakeMeta(
            MakePitm(1),
            MakeIinf(MakeInfeV2(1, "hvc1"), MakeInfeV2(2, "hvc1")),
            MakeIref(thmb));

        var result = HeicParser.Parse(StreamOf(meta));

        var thumbRefs = result.ThumbnailRefs.ToList();
        await Assert.That(thumbRefs).HasSingleItem();
        await Assert.That(thumbRefs[0].FromItemId).IsEqualTo(2u);
    }

    [Test]
    public async Task Detects_multiple_auxiliary_refs()
    {
        // Items 3 (depth) and 4 (gain map) are both auxiliary to item 1.
        var auxl1 = MakeRefChild("auxl", fromId: 3, toIds: 1);
        var auxl2 = MakeRefChild("auxl", fromId: 4, toIds: 1);
        var meta  = MakeMeta(
            MakePitm(1),
            MakeIinf(
                MakeInfeV2(1, "hvc1"),
                MakeInfeV2(3, "hvc1"),
                MakeInfeV2(4, "hvc1")),
            MakeIref(auxl1, auxl2));

        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.AuxiliaryRefs.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Returns_empty_refs_when_no_iref()
    {
        var meta   = MakeMeta(MakePitm(1), MakeIinf(MakeInfeV2(1, "hvc1")));
        var result = HeicParser.Parse(StreamOf(meta));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.References).IsEmpty();
    }

    [Test]
    public async Task Handles_ftyp_box_before_meta()
    {
        // Real HEIC files have: ftyp, meta, mdat
        var ftyp   = MakeBox("ftyp", [0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0]);
        var meta   = MakeMeta(MakePitm(7), MakeIinf(MakeInfeV2(7, "hvc1")));
        var result = HeicParser.Parse(StreamOf([..ftyp, ..meta]));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.PrimaryItemId).IsEqualTo(7u);
    }
}

public sealed class HeicStructureAnalyzerTests
{
    private static byte[] Be2(ushort v) => [(byte)(v >> 8), (byte)v];
    private static byte[] Be4(uint v)   => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] MakeBox(string type, byte[] data)
    {
        var tb = System.Text.Encoding.ASCII.GetBytes(type[..4].PadRight(4));
        uint sz = (uint)(8 + data.Length);
        return [..Be4(sz), ..tb, ..data];
    }

    private static byte[] MakeFullBox(string type, byte version, byte[] data)
        => MakeBox(type, [version, 0, 0, 0, ..data]);

    private static byte[] MakeInfeV2(ushort itemId, string itemType)
    {
        var tb = System.Text.Encoding.ASCII.GetBytes(itemType.PadRight(4)[..4]);
        return MakeBox("infe", [2, 0, 0, 0, ..Be2(itemId), ..Be2(0), ..tb, 0]);
    }

    private static byte[] MakeRefChild(string refType, ushort fromId, params ushort[] toIds)
    {
        var d = new List<byte>();
        d.AddRange(Be2(fromId)); d.AddRange(Be2((ushort)toIds.Length));
        foreach (var id in toIds) d.AddRange(Be2(id));
        return MakeBox(refType, [..d]);
    }

    private static byte[] MakeMeta(params byte[][] children)
        => MakeFullBox("meta", 0, children.SelectMany(x => x).ToArray());

    [Test]
    public async Task Returns_empty_for_non_heic_stream()
    {
        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream("not a heic file"u8.ToArray()));
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task Reports_auxiliary_image_finding()
    {
        var auxl = MakeRefChild("auxl", fromId: 2, toIds: 1);
        var meta = MakeMeta(
            MakeFullBox("pitm", 0, Be2(1)),
            MakeFullBox("iinf", 0,
                [..Be2(2), ..MakeInfeV2(1, "hvc1"), ..MakeInfeV2(2, "hvc1")]),
            MakeFullBox("iref", 0, auxl));

        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream(meta));
        await Assert.That(findings).Contains(f => f.Id == "heic-auxiliary-images");
    }

    [Test]
    public async Task Reports_thumbnail_finding()
    {
        var thmb = MakeRefChild("thmb", fromId: 2, toIds: 1);
        var meta = MakeMeta(
            MakeFullBox("pitm", 0, Be2(1)),
            MakeFullBox("iinf", 0,
                [..Be2(2), ..MakeInfeV2(1, "hvc1"), ..MakeInfeV2(2, "hvc1")]),
            MakeFullBox("iref", 0, thmb));

        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream(meta));
        await Assert.That(findings).Contains(f => f.Id == "heic-thumbnail-item");
    }

    [Test]
    public async Task Reports_item_inventory_finding()
    {
        var meta = MakeMeta(
            MakeFullBox("pitm", 0, Be2(1)),
            MakeFullBox("iinf", 0,
                [..Be2(2), ..MakeInfeV2(1, "hvc1"), ..MakeInfeV2(2, "Exif")]));

        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream(meta));
        await Assert.That(findings).Contains(f => f.Id == "heic-item-inventory");
    }

    [Test]
    public async Task No_findings_for_valid_meta_with_no_items_or_refs()
    {
        var meta     = MakeMeta(MakeFullBox("pitm", 0, Be2(1)));
        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream(meta));
        // No items → no inventory; no iref → no aux/thumb findings.
        await Assert.That(findings).IsEmpty();
    }

    [Test]
    public async Task Auxiliary_finding_text_pluralises_correctly()
    {
        var auxl1 = MakeRefChild("auxl", fromId: 2, toIds: 1);
        var auxl2 = MakeRefChild("auxl", fromId: 3, toIds: 1);
        var meta  = MakeMeta(
            MakeFullBox("pitm", 0, Be2(1)),
            MakeFullBox("iinf", 0,
                [..Be2(3),
                 ..MakeInfeV2(1, "hvc1"),
                 ..MakeInfeV2(2, "hvc1"),
                 ..MakeInfeV2(3, "hvc1")]),
            MakeFullBox("iref", 0, [..auxl1, ..auxl2]));

        var findings = HeicStructureAnalyzer.Analyze(new MemoryStream(meta));
        var aux = findings.First(f => f.Id == "heic-auxiliary-images");
        await Assert.That(aux.Observation).Contains("2 auxiliary");
    }
}