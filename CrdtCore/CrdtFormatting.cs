namespace CrdtCore
{
    public sealed record CrdtFormatting
    {
        public CrdtId FormattingId { get; set; }

        public CrdtId Start { get; set; }

        public CrdtId End { get; set; }

        public TextAttributes Attributes { get; set; }
    }
}
