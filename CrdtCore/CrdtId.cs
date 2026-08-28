namespace CrdtCore
{
    public sealed record CrdtId(int NodeId, long Counter) : IComparable<CrdtId>
    {
        public int CompareTo(CrdtId? other)
        {
            if (other is null) return 1;
            int counterComparison = Counter.CompareTo(other.Counter);
            if (counterComparison != 0)
                return counterComparison;
            return NodeId.CompareTo(other.NodeId);
        }
    }
}
