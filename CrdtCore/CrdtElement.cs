namespace CrdtCore
{
    public sealed record CrdtElement
    {
        public CrdtId CrdtId { get; set; }

        public char Value { get; set; }

        public CrdtId? PredecessorId { get; set; }

        public CrdtId?  SuccessorId { get; set; }

        public bool IsDeleted { get; set; }
    }
}
