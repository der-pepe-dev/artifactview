namespace ArtifactView.Infrastructure.Formats.Heic;

public sealed record HeicItem(
    uint   ItemId,
    string ItemType,   // e.g. "hvc1", "av01", "Exif", "mime", "grid"
    string ItemName,
    bool   IsHidden    // item_info_flag bit 0 = item is hidden
);

public sealed record HeicItemRef(
    string              RefType,     // e.g. "auxl", "thmb", "dimg", "cdsc"
    uint                FromItemId,
    IReadOnlyList<uint> ToItemIds
);

public sealed record HeicParseResult(
    bool                       IsValid,
    uint                       PrimaryItemId,
    IReadOnlyList<HeicItem>    Items,
    IReadOnlyList<HeicItemRef> References
)
{
    public static HeicParseResult Invalid { get; } =
        new(false, 0, [], []);

    public IEnumerable<HeicItemRef> AuxiliaryRefs
        => References.Where(r => r.RefType == "auxl");

    public IEnumerable<HeicItemRef> ThumbnailRefs
        => References.Where(r => r.RefType == "thmb");
}
