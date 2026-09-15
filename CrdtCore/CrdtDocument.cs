namespace CrdtCore
{
    public class CrdtDocument
    {
        public List<CrdtElement> Elements { get; set; } = [];

        public List<CrdtFormatting> Formattings { get; set; } = [];

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
            InsertElementInOrder(crdtElement);

            return crdtElement;
        }

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
                    && candidate.CrdtId.CompareTo(newElement.CrdtId) <= 0) // exit when new element has priority
                    break;

                skippedIds.Add(candidate.CrdtId);
                candidateIndex++;
            }

            Elements.Insert(candidateIndex, newElement);
        }


        public CrdtElement? Delete(CrdtId targetId)
        {
            var elementToDelete = Elements.FirstOrDefault(e => e.CrdtId == targetId);

            if (elementToDelete == null)
            {
                return null;
            }

            elementToDelete.IsDeleted = true;

            return elementToDelete;
        }

        public CrdtFormatting ApplyFormatting(CrdtFormatting formatting)
        {
            var conflicts = FindElementsWithConflictedFormats(formatting);
            ResolveConflicts(conflicts);

            Formattings.Add(formatting);
            return formatting;
        }

        // Privremeno resenje dok ne uvedemo Lamport clock: za svaki (Existing, Incoming, Key) konflikt
        // pobednik je onaj sa "vecim" CrdtFormattingId-em (Counter, pa NodeId - CrdtId.CompareTo), a iz
        // gubitnika se brise SAMO taj key (ne ceo formatting) - da ne obrisemo i deo spana koji se
        // uopste ne preklapa sa novim formattingom.
        // NAPOMENA: Counter je trenutno cisto lokalni brojac (ne Lamport clock), pa ovo garantuje samo
        // deterministicnu konvergenciju kod STVARNO konkurentnih izmena - ne garantuje da kasnija
        // (uzrocno posle) izmena uvek pobedi. To se resava kad uvedemo Lamport clock.
        private void ResolveConflicts(List<FormattingConflict> conflicts)
        {
            foreach (var group in conflicts.GroupBy(c => (c.Existing, c.Incoming, c.Key)))
            {
                var (existing, incoming, key) = group.Key;

                if (existing.FormattingId.CompareTo(incoming.FormattingId) > 0)
                {
                    incoming.Attributes.Remove(key);
                }
                else
                {
                    existing.Attributes.Remove(key);
                }
            }
        }

        private record FormattingConflict(CrdtElement Element, string Key, CrdtFormatting Existing, CrdtFormatting Incoming);

        private List<FormattingConflict> FindElementsWithConflictedFormats(CrdtFormatting formatting)
        {
            var conflicts = new List<FormattingConflict>();
            var elementsInRange = GetElementsInRange(formatting.Start, formatting.End);

            foreach (var format in Formattings)
            {
                var alreadyFormattedElements = GetElementsInRange(format.Start, format.End);
                var intersection = elementsInRange.Intersect(alreadyFormattedElements).ToList();
                if (intersection.Count == 0) continue;

                foreach (var key in format.Attributes.Keys.Intersect(formatting.Attributes.Keys))
                {
                    if (format.Attributes[key] == formatting.Attributes[key]) continue; // ista vrednost = nije konflikt

                    conflicts.AddRange(intersection.Select(e => new FormattingConflict(e, key, format, formatting)));
                }
            }

            return conflicts;
        }

        public List<CrdtElement> GetElementsInRange(CrdtAnchor start, CrdtAnchor end)
        {
            var startIndex = ResolveAnchorBoundary(start);
            var endIndex = ResolveAnchorBoundary(end);

            if (startIndex >= endIndex)
            {
                return new List<CrdtElement>();
            }

            return Elements.GetRange(startIndex, endIndex - startIndex);
        }

        private int ResolveAnchorBoundary(CrdtAnchor anchor)
        {
            if (anchor.Id == null)
            {
                return anchor.Type == AnchorType.After ? Elements.Count : 0;
            }

            var index = FindElementIndexById(anchor.Id);
            if (index == -1)
            {
                return anchor.Type == AnchorType.After ? Elements.Count : 0;
            }

            return anchor.Type == AnchorType.Before ? index : index + 1;
        }

        public string GetText() => string.Join("", Elements.Where(x => !x.IsDeleted).Select(x => x.Value));

    }
}
