namespace CrdtCore
{
    public sealed record CrdtId(int NodeId, long Counter) : IComparable<CrdtId>
    {
        public int CompareTo(CrdtId? other)
        {
            if (other is null) return 1;
            int nodeComparison = NodeId.CompareTo(other.NodeId);
            if (nodeComparison != 0)
                return nodeComparison;
            return Counter.CompareTo(other.Counter);
        }
    }
}
