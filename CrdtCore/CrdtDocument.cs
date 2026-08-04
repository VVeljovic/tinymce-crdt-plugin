namespace CrdtCore
{
    public class CrdtDocument
    {
        public List<CrdtElement> Elements { get; set; } = [];

        public int NodeId { get; set; }

        public int Counter { get; set; }

        public CrdtElement LocalInsert(char value, int visibleIndex)
        {
            var predecessorId = FindPredecessorId(visibleIndex);

            var newId = new CrdtId(NodeId, Counter++);

            var newElement = new CrdtElement
            {
                CrdtId = newId,
                Value = value,
                PredecessorId = predecessorId,
                IsDeleted = false
            };

            return newElement;
        }

        private CrdtId FindPredecessorId(int visibleIndex)
        {
            throw new NotImplementedException();
        }
    }
}
