using System.Text;
using static ArtifactView.Infrastructure.Formats.Heic.HeicBoxReader;

namespace ArtifactView.Infrastructure.Formats.Heic;

// Parses the ISO BMFF structure of a HEIC/HEIF file to extract item info and
// item references.  Only the meta/iinf/iref/pitm sub-tree is read; pixel data
// and item properties (ipco/ipma) are not needed for structural analysis.
public static class HeicParser
{
    public static HeicParseResult Parse(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8)
            return HeicParseResult.Invalid;

        try
        {
            return DoParse(stream);
        }
        catch
        {
            return HeicParseResult.Invalid;
        }
    }

    private static HeicParseResult DoParse(Stream stream)
    {
        var topBoxes = ReadBoxes(stream, 0, stream.Length);

        var metaBox = topBoxes.FirstOrDefault(b => b.Type == "meta");
        if (metaBox == default)
            return HeicParseResult.Invalid;

        // meta is a FullBox — skip version+flags before reading children.
        stream.Position = metaBox.DataStart;
        ReadFullBoxHeader(stream);

        var metaChildren = ReadBoxes(stream, stream.Position, metaBox.DataEnd);

        var primaryItemId = ReadPitm(stream, metaChildren);
        var items         = ReadIinf(stream, metaChildren);
        var refs          = ReadIref(stream, metaChildren);

        return new HeicParseResult(true, primaryItemId, items, refs);
    }

    // ── pitm ────────────────────────────────────────────────────────────────

    private static uint ReadPitm(
        Stream stream, List<HeicBox> metaChildren)
    {
        var box = metaChildren.FirstOrDefault(b => b.Type == "pitm");
        if (box == default) return 0;

        stream.Position = box.DataStart;
        var (version, _) = ReadFullBoxHeader(stream);
        return version >= 1 ? ReadU32(stream) : ReadU16(stream);
    }

    // ── iinf ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<HeicItem> ReadIinf(
        Stream stream, List<HeicBox> metaChildren)
    {
        var box = metaChildren.FirstOrDefault(b => b.Type == "iinf");
        if (box == default) return [];

        stream.Position = box.DataStart;
        var (version, _) = ReadFullBoxHeader(stream);

        // entry_count: 2 bytes for v0, 4 bytes for v1+
        _ = version >= 1 ? ReadU32(stream) : (uint)ReadU16(stream);

        var infeBoxes = ReadBoxes(stream, stream.Position, box.DataEnd);
        var items     = new List<HeicItem>(infeBoxes.Count);

        foreach (var infe in infeBoxes)
        {
            if (infe.Type != "infe") continue;
            var item = ParseInfe(stream, infe.DataStart, infe.DataEnd);
            if (item is not null) items.Add(item);
        }

        return items;
    }

    private static HeicItem? ParseInfe(Stream s, long dataStart, long dataEnd)
    {
        s.Position = dataStart;
        if (dataEnd - dataStart < 5) return null;

        var (version, flags) = ReadFullBoxHeader(s);
        bool isHidden = (flags & 1) != 0;

        if (version < 2)
            return null; // v0/v1 are legacy JPEG/motion HEIF; skip

        // item_ID: 2 bytes for v2, 4 bytes for v3+
        uint itemId = version >= 3 ? ReadU32(s) : ReadU16(s);

        // protection_index (always 2 bytes)
        ReadU16(s);

        // item_type: 4-byte ASCII
        Span<byte> typeBytes = stackalloc byte[4];
        if (s.ReadAtLeast(typeBytes, 4, throwOnEndOfStream: false) < 4) return null;
        var itemType = Encoding.ASCII.GetString(typeBytes);

        // item_name: null-terminated string
        var itemName = ReadNullString(s, dataEnd);

        return new HeicItem(itemId, itemType, itemName, isHidden);
    }

    // ── iref ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<HeicItemRef> ReadIref(
        Stream stream, List<HeicBox> metaChildren)
    {
        var box = metaChildren.FirstOrDefault(b => b.Type == "iref");
        if (box == default) return [];

        stream.Position = box.DataStart;
        var (irefVersion, _) = ReadFullBoxHeader(stream);

        var refBoxes = ReadBoxes(stream, stream.Position, box.DataEnd);
        var refs     = new List<HeicItemRef>(refBoxes.Count);

        foreach (var refBox in refBoxes)
        {
            var r = ParseItemRef(stream, refBox.Type, refBox.DataStart, refBox.DataEnd, irefVersion);
            if (r is not null) refs.Add(r);
        }

        return refs;
    }

    // SingleItemTypeReferenceBox — NOT a FullBox; item_ID widths follow iref version.
    private static HeicItemRef? ParseItemRef(
        Stream s, string refType, long dataStart, long dataEnd, byte irefVersion)
    {
        s.Position = dataStart;

        // from_item_id
        if (dataEnd - dataStart < (irefVersion >= 1 ? 6 : 4)) return null;
        uint fromItemId = irefVersion >= 1 ? ReadU32(s) : ReadU16(s);

        // reference_count
        var refCount = ReadU16(s);

        var toIds = new List<uint>(refCount);
        for (int i = 0; i < refCount && s.Position + (irefVersion >= 1 ? 4 : 2) <= dataEnd; i++)
            toIds.Add(irefVersion >= 1 ? ReadU32(s) : ReadU16(s));

        return new HeicItemRef(refType, fromItemId, toIds);
    }
}
