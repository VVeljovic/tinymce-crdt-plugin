namespace CrdtCore
{
    public sealed record CrdtFormatting
    {
        public CrdtId FormattingId { get; set; }

        public CrdtAnchor Start { get; set; }

        public CrdtAnchor End { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }
}
