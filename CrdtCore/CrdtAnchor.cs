namespace CrdtCore
{
    public sealed record CrdtAnchor(CrdtId? Id, AnchorType Type)
    {
        public static CrdtAnchor Before(CrdtId id) => new(id, AnchorType.Before);
        public static CrdtAnchor After(CrdtId id) => new(id, AnchorType.After);

        public static CrdtAnchor StartOfText => new(null, AnchorType.Before);
        public static CrdtAnchor EndOfText => new(null, AnchorType.After);
    }
}
