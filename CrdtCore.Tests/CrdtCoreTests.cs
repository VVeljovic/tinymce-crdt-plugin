namespace CrdtCore.Tests
{
    public class CrdtCoreTests
    {
        private static CrdtElement Element(CrdtId id, char value, CrdtId? predecessorId = null) =>
            new CrdtElement
            {
                CrdtId = id,
                Value = value,
                PredecessorId = predecessorId,
                IsDeleted = false
            };

        [Fact]
        public void Insert_SequentialCharacters_ProducesTextInInsertionOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var doc = new CrdtDocument();

            //Act
            doc.Insert(Element(idA, 'A'));
            doc.Insert(Element(idB, 'B', idA));

            //Assert
            Assert.Equal("AB", doc.GetText());
        }

        [Fact]
        public void RemoteDelete_ExistingElement_RemovesFromVisibleTextButKeepsTombstone()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var doc = new CrdtDocument();
            doc.Insert(Element(idA, 'A'));
            doc.Insert(Element(idB, 'B', idA));

            //Act
            doc.Delete(idB);

            //Assert
            Assert.Equal("A", doc.GetText());

            var deletedElement = doc.Elements.Single(e => e.CrdtId == idB);
            Assert.True(deletedElement.IsDeleted);
            Assert.Equal(2, doc.Elements.Count);
        }

        [Fact]
        public void Insert_ConcurrentInsertsAtSamePosition_ConvergeRegardlessOfOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var idC = new CrdtId(2, 2);

            var docBFirst = new CrdtDocument();
            var docCFirst = new CrdtDocument();

            //Act
            docBFirst.Insert(Element(idA, 'A'));
            docBFirst.Insert(Element(idB, 'B', idA));
            docBFirst.Insert(Element(idC, 'C', idA));

            docCFirst.Insert(Element(idA, 'A'));
            docCFirst.Insert(Element(idC, 'C', idA));
            docCFirst.Insert(Element(idB, 'B', idA));

            //Assert
            Assert.Equal(docBFirst.GetText(), docCFirst.GetText());
        }

        [Fact]
        public void Insert_ConcurrentInsertsAtDocumentStart_ConvergeRegardlessOfOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(2, 1);

            var docBFirst = new CrdtDocument();
            var docAFirst = new CrdtDocument();

            //Act
            docBFirst.Insert(Element(idB, 'B'));
            docBFirst.Insert(Element(idA, 'A'));

            docAFirst.Insert(Element(idA, 'A'));
            docAFirst.Insert(Element(idB, 'B'));

            //Assert
            Assert.Equal(docBFirst.GetText(), docAFirst.GetText());
        }

        [Fact]
        public void Insert_DifferentInsertionOrdersFromMultipleNodes_ConvergeToSameState()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var idC = new CrdtId(2, 2);
            var idD = new CrdtId(3, 3);

            var firstDoc = new CrdtDocument();
            firstDoc.Insert(Element(idA, 'A'));

            var secondDoc = new CrdtDocument();
            secondDoc.Insert(Element(idA, 'A'));

            var thirdDoc = new CrdtDocument();
            thirdDoc.Insert(Element(idA, 'A'));

            //Act
            firstDoc.Insert(Element(idB, 'B', idA));
            firstDoc.Insert(Element(idC, 'C', idA));
            firstDoc.Insert(Element(idD, 'D', idA));

            secondDoc.Insert(Element(idC, 'C', idA));
            secondDoc.Insert(Element(idB, 'B', idA));
            secondDoc.Insert(Element(idD, 'D', idA));

            thirdDoc.Insert(Element(idD, 'D', idA));
            thirdDoc.Insert(Element(idC, 'C', idA));
            thirdDoc.Insert(Element(idB, 'B', idA));

            //Assert
            Assert.Equal(firstDoc.GetText(), secondDoc.GetText());
            Assert.Equal(firstDoc.GetText(), thirdDoc.GetText());
            Assert.Equal(secondDoc.GetText(), thirdDoc.GetText());
        }

        [Fact]
        public void RemoteDelete_NonExistentElement_ShouldNotThrowException()
        {
            //Arrange
            var doc = new CrdtDocument();

            //Act
            var exception = Record.Exception(() => doc.Delete(new CrdtId(1, 0)));

            //Assert
            Assert.Null(exception);
        }

        [Fact]
        public void RemoteDelete_AlreadyDeletedElement_ShouldNotThrowException()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var doc = new CrdtDocument();
            doc.Insert(Element(idA, 'A'));
            doc.Delete(idA);

            //Act
            var exception = Record.Exception(() => doc.Delete(idA));

            //Assert
            Assert.Null(exception);
        }

        private static Dictionary<string, string> EffectiveAttributes(CrdtDocument doc, CrdtId targetId)
        {
            var elements = doc.Elements;
            var targetIndex = elements.FindIndex(e => e.CrdtId == targetId);

            var result = new Dictionary<string, string>();
            foreach (var formatting in doc.Formattings)
            {
                var startIndex = formatting.Start.Id == null ? 0 : elements.FindIndex(e => e.CrdtId == formatting.Start.Id);
                var endIndex = formatting.End.Id == null ? elements.Count - 1 : elements.FindIndex(e => e.CrdtId == formatting.End.Id);

                if (targetIndex >= startIndex && targetIndex <= endIndex)
                {
                    foreach (var (key, value) in formatting.Attributes)
                    {
                        result[key] = value;
                    }
                }
            }

            return result;
        }


        private static List<CrdtId> InsertSentence(CrdtDocument doc, string text, int nodeId)
        {
            var ids = new List<CrdtId>();
            CrdtId? predecessorId = null;

            for (var i = 0; i < text.Length; i++)
            {
                var id = new CrdtId(nodeId, i);
                doc.Insert(Element(id, text[i], predecessorId));
                ids.Add(id);
                predecessorId = id;
            }

            return ids;
        }

        [Fact]
        public void ApplyOverlappingFormatting_SameAttributeOverlap_EntireSentenceBecomesBold()
        {
            //Arrange
            const string text = "The fox jumped";
            var doc = new CrdtDocument();
            var ids = InsertSentence(doc, text, nodeId: 1);

            var korisnikABold = new CrdtFormatting
            {
                FormattingId = new CrdtId(2, 0),
                Start = new CrdtAnchor(ids[0], AnchorType.Before),
                End = new CrdtAnchor(ids[6], AnchorType.After),
                Attributes = new Dictionary<string, string> { { "bold", "true" } }
            };

            var korisnikBBold = new CrdtFormatting
            {
                FormattingId = new CrdtId(3, 0),
                Start = new CrdtAnchor(ids[4], AnchorType.Before),
                End = new CrdtAnchor(ids[13], AnchorType.After),
                Attributes = new Dictionary<string, string> { { "bold", "true" } }
            };

            //Act
            doc.ApplyFormatting(korisnikABold);
            doc.ApplyFormatting(korisnikBBold);

            //Assert
            foreach (var id in ids)
            {
                var attributes = EffectiveAttributes(doc, id);
                Assert.Equal("true", attributes.GetValueOrDefault("bold"));
            }
        }

        [Fact]
        public void ApplyOverlappingFormatting_OverlappingCharacterGetsUnionOfAttributes()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var doc = new CrdtDocument();
            doc.Insert(Element(idA, 'A'));
            doc.Insert(Element(idB, 'B', idA));

            var formatting1 = new CrdtFormatting
            {
                FormattingId = new CrdtId(2, 0),
                Start = new CrdtAnchor(idA, AnchorType.Before),
                End = new CrdtAnchor(idB, AnchorType.After),
                Attributes = new Dictionary<string, string> { { "bold", "true" } }
            };
            var formatting2 = new CrdtFormatting
            {
                FormattingId = new CrdtId(2, 1),
                Start = new CrdtAnchor(idA, AnchorType.Before),
                End = new CrdtAnchor(idA, AnchorType.After),
                Attributes = new Dictionary<string, string> { { "italic", "true" } }
            };

            //Act
            doc.ApplyFormatting(formatting1);
            doc.ApplyFormatting(formatting2);

            //Assert
            var attributesOnA = EffectiveAttributes(doc, idA);
            Assert.Equal("true", attributesOnA.GetValueOrDefault("bold"));
            Assert.Equal("true", attributesOnA.GetValueOrDefault("italic"));

            var attributesOnB = EffectiveAttributes(doc, idB);
            Assert.Equal("true", attributesOnB.GetValueOrDefault("bold"));
            Assert.False(attributesOnB.ContainsKey("italic"));
        }
    }
}
