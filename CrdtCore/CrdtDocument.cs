namespace CrdtCore
{
    public class CrdtDocument
    {
        public List<CrdtElement> Elements { get; set; } = [];

        public List<CrdtFormatting> Formattings { get; set; } = [];

        private readonly List<CrdtElement> _pendingElements = [];
        private readonly List<CrdtId> _pendingDeletes = [];

        public CrdtDocument() { }

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

        public CrdtElement Insert(CrdtElement crdtElement)
        {
            if (!TryInsertElementInOrder(crdtElement))
            {
                _pendingElements.Add(crdtElement);
                return crdtElement;
            }

            ApplyPendingOperations();

            return crdtElement;
        }

        private void ApplyPendingOperations()
        {
            bool anyChanged;
            do
            {
                anyChanged = false;

                for (int i = _pendingElements.Count - 1; i >= 0; i--)
                {
                    var pendingElement = _pendingElements[i];
                    if (TryInsertElementInOrder(pendingElement))
                    {
                        _pendingElements.RemoveAt(i);
                        anyChanged = true;
                    }
                }

                for (int i = _pendingDeletes.Count - 1; i >= 0; i--)
                {
                    if (TryDeleteElement(_pendingDeletes[i]))
                    {
                        _pendingDeletes.RemoveAt(i);
                        anyChanged = true;
                    }
                }
            } while (anyChanged);
        }

        private bool TryInsertElementInOrder(CrdtElement newElement)
        {
            if (newElement.PredecessorId != null && FindElementIndexById(newElement.PredecessorId) == -1)
            {
                return false;
            }

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
                    && candidate.CrdtId.CompareTo(newElement.CrdtId) <= 0) // exit when new element has priority
                    break;

                skippedIds.Add(candidate.CrdtId);
                candidateIndex++;
            }

            Elements.Insert(candidateIndex, newElement);

            return true;
        }


        public CrdtElement? Delete(CrdtId targetId)
        {
            var elementToDelete = Elements.FirstOrDefault(e => e.CrdtId == targetId);

            if (elementToDelete == null)
            {
                _pendingDeletes.Add(targetId);
                return null;
            }

            elementToDelete.IsDeleted = true;

            return elementToDelete;
        }

        private bool TryDeleteElement(CrdtId targetId)
        {
            var elementToDelete = Elements.FirstOrDefault(e => e.CrdtId == targetId);

            if (elementToDelete == null)
            {
                return false;
            }

            elementToDelete.IsDeleted = true;

            return true;
        }

        public CrdtFormatting ApplyFormatting(CrdtFormatting formatting)
        {
            Formattings.Add(formatting);
            return formatting;
        }

        public List<ResolvedFormatting> ResolveFormatting()
        {
            var resolvedFormattings = new List<ResolvedFormatting>();

            for (int position = 0; position < Elements.Count; position++)
            {
                var element = Elements[position];

                if (element.IsDeleted)
                {
                    continue;
                }

                var attributes = ResolveAttributesAt(position);

                if (attributes.Count > 0)
                {
                    resolvedFormattings.Add(new ResolvedFormatting(element.CrdtId, attributes));
                }

            }
            return resolvedFormattings;
        }

        private Dictionary<string, string> ResolveAttributesAt(int position)
        {
            var winnerPerAttribute = new Dictionary<string, CrdtFormatting>();
            foreach (var formatting in GetFormattingsAtPosition(position))
            {
                foreach (var attribute in formatting.Attributes.Keys)
                {
                    if (!winnerPerAttribute.TryGetValue(attribute, out var currentWinner) ||
                        formatting.FormattingId.CompareTo(currentWinner.FormattingId) > 0)
                    {
                        winnerPerAttribute[attribute] = formatting;
                    }
                }
            }
            return winnerPerAttribute.ToDictionary(
                winner => winner.Key,
                winner => winner.Value.Attributes[winner.Key]);
        }

        private IEnumerable<CrdtFormatting> GetFormattingsAtPosition(int position)
        {
            return Formattings.Where(f => IsPositionInFormattingRange(f, position));
        }

        private bool IsPositionInFormattingRange(CrdtFormatting formatting, int position)
        {
            var startPosition = ResolveAnchorPosition(formatting.Start);
            var endPosition = ResolveAnchorPosition(formatting.End);
            if (startPosition == null || endPosition == null)
            {
                return false;
            }
            return position >= startPosition && position < endPosition;
        }

        private int? ResolveAnchorPosition(CrdtAnchor anchor)
        {
            if (anchor.Id is null)
            {
                return anchor.Type == AnchorType.Before ? 0 : Elements.Count;
            }

            var index = FindElementIndexById(anchor.Id);
            if (index == -1)
            {
                return null;
            }

            return anchor.Type == AnchorType.Before ? index : index + 1;
        }

        public string GetText() => string.Join("", Elements.Where(x => !x.IsDeleted).Select(x => x.Value));

    }
}
