namespace CrdtCore
{
    public class CrdtDocument
    {
        public List<CrdtElement> Elements { get; set; } = [];

        public int Counter { get; set; }
        public CrdtDocument() { }

        private void InsertElementInOrder(CrdtElement newElement)
        {
            var insertAfterIndex = FindElementIndexById(newElement.PredecessorId);
            var candidateIndex = insertAfterIndex + 1;

            var successorIndex = FindElementIndexById(newElement.SuccessorId);
            var boundIndex = newElement.SuccessorId != null && successorIndex >= 0 ? successorIndex : Elements.Count;

            var skippedIds = new HashSet<CrdtId?> { newElement.PredecessorId };

            while (candidateIndex < boundIndex)
            {
                var candidate = Elements[candidateIndex];

                bool partOfConflicts = skippedIds.Contains(candidate.PredecessorId);

                if (!partOfConflicts)
                    break;

                if (candidate.PredecessorId == newElement.PredecessorId
                    && !HasPriority(candidate, newElement)) // exit when new element has priority
                    break;

                skippedIds.Add(candidate.CrdtId);
                candidateIndex++;
            }

            Elements.Insert(candidateIndex, newElement);
        }


        private int FindElementIndexById(CrdtId? crdtId)
        {
            if (crdtId == null)
            {
                return -1;
            }

            for (int i = 0; i < Elements.Count; i++)
            {
                if (Elements[i].CrdtId == crdtId)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool HasPriority(CrdtElement existing, CrdtElement incoming) => existing.CrdtId.CompareTo(incoming.CrdtId) > 0;

        public CrdtElement RemoteInsert(CrdtElement crdtElement)
        {
            InsertElementInOrder(crdtElement);

            return crdtElement;
        }

        public CrdtElement? RemoteDelete(CrdtId targetId)
        {
            var elementToDelete = Elements.FirstOrDefault(e => e.CrdtId == targetId);

            if (elementToDelete == null)
            {
                return null;
            }

            elementToDelete.IsDeleted = true;

            return elementToDelete;
        }

        public string GetText() => string.Join("", Elements.Where(x => !x.IsDeleted).Select(x => x.Value));
    }
}
