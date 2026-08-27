namespace CrdtCore.Tests
{
    public class CrdtCoreTests
    {
        [Fact]
        public void Insert_SequentialCharacters_ProducesTextInInsertionOrder()
        {
            //Arrange
            var doc = new CrdtDocument();

            //Act
            doc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);
            doc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);

            //Assert
            Assert.Equal("AB", doc.GetText());
        }

        [Fact]
        public void Delete_ExistingElement_RemoveFromVisibleTextButKeepsTombstone()
        {
            //Arrange
            var doc = new CrdtDocument();

            //Act
            doc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);
            doc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);
            doc.Delete(visibleIndex: 1);

            //Assert
            Assert.Equal("A", doc.GetText());

            var deletedElement = doc.Elements.FirstOrDefault(e => e.Value == 'B');
            Assert.True(deletedElement.IsDeleted);
            Assert.Equal(2, doc.Elements.Count);
        }

        [Fact]
        public void ConcturrentInsertionsFromDifferentNodes_MaintainsCorrectOrder()
        {
            //Arrange
            var firstDoc = new CrdtDocument();
            firstDoc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);

            var secondDoc = new CrdtDocument();
            secondDoc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);

            //Act
            firstDoc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);
            firstDoc.Insert(nodeId: 2, value: 'C', visibleIndex: 1);

            secondDoc.Insert(nodeId: 2, value: 'C', visibleIndex: 1);
            secondDoc.Insert(nodeId: 1, value: 'B', visibleIndex: 2);

            //Assert
            Assert.Equal(firstDoc.GetText(), secondDoc.GetText());
        }

        [Fact]
        public void ConcurrentInsertsAtDocumentStart_ConvergeRegardlessOfOrder()
        {
            //Arrange
            var docAppliedBFirst = new CrdtDocument();
            var docAppliedAFirst = new CrdtDocument();

            //Act
            docAppliedBFirst.Insert(nodeId: 2, value: 'B', visibleIndex: 0);
            docAppliedBFirst.Insert(nodeId: 1, value: 'A', visibleIndex: 0);

            docAppliedAFirst.Insert(nodeId: 1, value: 'A', visibleIndex: 0);
            docAppliedAFirst.Insert(nodeId: 2, value: 'B', visibleIndex: 0);

            //Assert
            Assert.Equal(docAppliedBFirst.GetText(), docAppliedAFirst.GetText());
        }

        [Fact]
        public void DifferentInsertionOrdersFromMultipleNodes_ShouldConvergeToSameState()
        {
            //Arrange
            var firstDoc = new CrdtDocument();
            firstDoc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);

            var secondDoc = new CrdtDocument();
            secondDoc.Insert(nodeId: 2, value: 'A', visibleIndex: 0);

            var thirdDoc = new CrdtDocument();
            thirdDoc.Insert(nodeId: 3, value: 'A', visibleIndex: 0);

            //Act
            firstDoc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);
            firstDoc.Insert(nodeId: 2, value: 'C', visibleIndex: 1);
            firstDoc.Insert(nodeId: 3, value: 'D', visibleIndex: 1);

            secondDoc.Insert(nodeId: 2, value: 'C', visibleIndex: 1);
            secondDoc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);
            secondDoc.Insert(nodeId: 3, value: 'D', visibleIndex: 1);

            thirdDoc.Insert(nodeId: 3, value: 'D', visibleIndex: 1);
            thirdDoc.Insert(nodeId: 2, value: 'C', visibleIndex: 1);
            thirdDoc.Insert(nodeId: 1, value: 'B', visibleIndex: 1);

            //Assert
            Assert.Equal(firstDoc.GetText(), secondDoc.GetText());
            Assert.Equal(firstDoc.GetText(), thirdDoc.GetText());
            Assert.Equal(secondDoc.GetText(), thirdDoc.GetText());
        }

        [Fact]
        public void Delete_NonExistentElement_ShouldNotThrowException()
        {
            //Arrange
            var doc = new CrdtDocument();

            //Act
            var exception = Record.Exception(() => doc.Delete(visibleIndex: 0));

            //Assert
            Assert.Null(exception);
        }

        [Fact]
        public void Delete_AlreadyDeletedElement_ShouldNotThrowException()
        {
            //Arrange
            var doc = new CrdtDocument();
            doc.Insert(nodeId: 1, value: 'A', visibleIndex: 0);
            doc.Delete(visibleIndex: 0);

            //Act
            var exception = Record.Exception(() => doc.Delete(visibleIndex: 0));

            //Assert
            Assert.Null(exception);
        }
    }
}
