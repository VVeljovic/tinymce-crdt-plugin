namespace CrdtCore
{
    public class CrdtDocument
    {
        public List<CrdtElement> Elements { get; set; } = [];

        public int NodeId { get; set; } 

        public int Counter { get; set; }
        public CrdtDocument()
        {
        }

        public string GetText()
        {
            return new string(Elements
                .Where(x => !x.IsDeleted)
                .Select(x => x.Value)
                .ToArray());
        }

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

            InsertElementInOrder(newElement);

            return newElement;
        }

        public CrdtId? FindPredecessorId(int visibleIndex)
        {
            if (visibleIndex == 0)
            {
                return null;
            }

            var visibleCount = 0;

            foreach (var element in Elements)
            {
                if (element.IsDeleted)
                {
                    continue;
                }

                visibleCount++;

                if (visibleIndex == visibleCount)
                {
                    return element.CrdtId;
                }
            }

            return Elements.LastOrDefault(x => !x.IsDeleted)?.CrdtId;
        }

        public CrdtElement LocalDelete(int visibleIndex)
        {
            var elementToDelete = FindVisibleElementAt(visibleIndex);

            if (elementToDelete == null)
            {
                return null;
            }

            elementToDelete.IsDeleted = true;
            return elementToDelete;
        }

        public CrdtElement FindVisibleElementAt(int visibleIndex)
        {
            var visibleCount = -1;

            foreach (var element in Elements)
            {
                if (element.IsDeleted)
                {
                    continue;
                }

                visibleCount++;

                if (visibleCount == visibleIndex)
                {
                    return element;
                }
            }

            return null;
        }


        public void RemoteInsert(CrdtElement crdtElement)
        {
            if (Elements.Any(x => x.CrdtId == crdtElement.CrdtId))
            {
                return;
            }

            InsertElementInOrder(crdtElement);

        }

        private void InsertElementInOrder(CrdtElement newElement)
        {
            var insertAfterIndex = FindElementIndexById(newElement.PredecessorId);
            var candidateIndex = insertAfterIndex + 1;

            var skippedIds = new HashSet<CrdtId>();
            if (newElement.PredecessorId != null)
                skippedIds.Add(newElement.PredecessorId);

            while (candidateIndex < Elements.Count)
            {
                var candidate = Elements[candidateIndex];

                bool partOfChain = candidate.PredecessorId != null
                    && skippedIds.Contains(candidate.PredecessorId);

                if (!partOfChain)
                    break;

                if (candidate.PredecessorId == newElement.PredecessorId
                    && !HasPriority(candidate, newElement))
                    break;

                skippedIds.Add(candidate.CrdtId);
                candidateIndex++;
            }

            Elements.Insert(candidateIndex, newElement);
        }


        private int FindElementIndexById(CrdtId? crdtId)
        {
            if(crdtId == null)
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
        private bool HasPriority(CrdtElement existing, CrdtElement incoming)
        {
            if (existing.CrdtId.NodeId > incoming.CrdtId.NodeId)
            {
                return true;
            }

            return existing.CrdtId.Counter > incoming.CrdtId.Counter;
        }

        public void RemoteDelete(CrdtId crdtId)
        {
            var element = Elements.FirstOrDefault(x => x.CrdtId == crdtId);
            if (element != null)
                element.IsDeleted = true;
        }
    }
}
