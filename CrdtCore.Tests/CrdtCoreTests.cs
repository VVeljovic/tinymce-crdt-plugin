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

    }
}
