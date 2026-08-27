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
        public void RemoteInsert_SequentialCharacters_ProducesTextInInsertionOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var doc = new CrdtDocument();

            //Act
            doc.RemoteInsert(Element(idA, 'A'));
            doc.RemoteInsert(Element(idB, 'B', idA));

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
            doc.RemoteInsert(Element(idA, 'A'));
            doc.RemoteInsert(Element(idB, 'B', idA));

            //Act
            doc.RemoteDelete(idB);

            //Assert
            Assert.Equal("A", doc.GetText());

            var deletedElement = doc.Elements.Single(e => e.CrdtId == idB);
            Assert.True(deletedElement.IsDeleted);
            Assert.Equal(2, doc.Elements.Count);
        }

        [Fact]
        public void RemoteInsert_ConcurrentInsertsAtSamePosition_ConvergeRegardlessOfOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var idC = new CrdtId(2, 2);

            var docBFirst = new CrdtDocument();
            var docCFirst = new CrdtDocument();

            //Act
            docBFirst.RemoteInsert(Element(idA, 'A'));
            docBFirst.RemoteInsert(Element(idB, 'B', idA));
            docBFirst.RemoteInsert(Element(idC, 'C', idA));

            docCFirst.RemoteInsert(Element(idA, 'A'));
            docCFirst.RemoteInsert(Element(idC, 'C', idA));
            docCFirst.RemoteInsert(Element(idB, 'B', idA));

            //Assert
            Assert.Equal(docBFirst.GetText(), docCFirst.GetText());
        }

        [Fact]
        public void RemoteInsert_ConcurrentInsertsAtDocumentStart_ConvergeRegardlessOfOrder()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(2, 1);

            var docBFirst = new CrdtDocument();
            var docAFirst = new CrdtDocument();

            //Act
            docBFirst.RemoteInsert(Element(idB, 'B'));
            docBFirst.RemoteInsert(Element(idA, 'A'));

            docAFirst.RemoteInsert(Element(idA, 'A'));
            docAFirst.RemoteInsert(Element(idB, 'B'));

            //Assert
            Assert.Equal(docBFirst.GetText(), docAFirst.GetText());
        }

        [Fact]
        public void RemoteInsert_DifferentInsertionOrdersFromMultipleNodes_ConvergeToSameState()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var idB = new CrdtId(1, 1);
            var idC = new CrdtId(2, 2);
            var idD = new CrdtId(3, 3);

            var firstDoc = new CrdtDocument();
            firstDoc.RemoteInsert(Element(idA, 'A'));

            var secondDoc = new CrdtDocument();
            secondDoc.RemoteInsert(Element(idA, 'A'));

            var thirdDoc = new CrdtDocument();
            thirdDoc.RemoteInsert(Element(idA, 'A'));

            //Act
            firstDoc.RemoteInsert(Element(idB, 'B', idA));
            firstDoc.RemoteInsert(Element(idC, 'C', idA));
            firstDoc.RemoteInsert(Element(idD, 'D', idA));

            secondDoc.RemoteInsert(Element(idC, 'C', idA));
            secondDoc.RemoteInsert(Element(idB, 'B', idA));
            secondDoc.RemoteInsert(Element(idD, 'D', idA));

            thirdDoc.RemoteInsert(Element(idD, 'D', idA));
            thirdDoc.RemoteInsert(Element(idC, 'C', idA));
            thirdDoc.RemoteInsert(Element(idB, 'B', idA));

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
            var exception = Record.Exception(() => doc.RemoteDelete(new CrdtId(1, 0)));

            //Assert
            Assert.Null(exception);
        }

        [Fact]
        public void RemoteDelete_AlreadyDeletedElement_ShouldNotThrowException()
        {
            //Arrange
            var idA = new CrdtId(1, 0);
            var doc = new CrdtDocument();
            doc.RemoteInsert(Element(idA, 'A'));
            doc.RemoteDelete(idA);

            //Act
            var exception = Record.Exception(() => doc.RemoteDelete(idA));

            //Assert
            Assert.Null(exception);
        }
    }
}
