using ExportDocManager.Utils;

namespace ExportDocManager.Application.Tests
{
    public class ListSelectionHelperTests
    {
        [Fact]
        public void GetFallbackItemAfterRemoval_ShouldReturnNextItem_WhenRemovingMiddleItem()
        {
            var items = new[]
            {
                new TestEntity(1, "Alpha"),
                new TestEntity(2, "Beta"),
                new TestEntity(3, "Gamma")
            };

            var fallback = ListSelectionHelper.GetFallbackItemAfterRemoval(items, item => item.Id == 2);

            Assert.NotNull(fallback);
            Assert.Equal(3, fallback.Id);
        }

        [Fact]
        public void GetFallbackItemAfterRemoval_ShouldReturnPreviousItem_WhenRemovingLastItem()
        {
            var items = new[]
            {
                new TestEntity(1, "Alpha"),
                new TestEntity(2, "Beta"),
                new TestEntity(3, "Gamma")
            };

            var fallback = ListSelectionHelper.GetFallbackItemAfterRemoval(items, item => item.Id == 3);

            Assert.NotNull(fallback);
            Assert.Equal(2, fallback.Id);
        }

        [Fact]
        public void GetFallbackItemAfterRemoval_ShouldReturnNull_WhenOnlyOneItemExists()
        {
            var items = new[]
            {
                new TestEntity(1, "Alpha")
            };

            var fallback = ListSelectionHelper.GetFallbackItemAfterRemoval(items, item => item.Id == 1);

            Assert.Null(fallback);
        }

        [Fact]
        public void GetFallbackItemAfterRemoval_ShouldReturnNull_WhenRemovedItemIsNotFound()
        {
            var items = new[]
            {
                new TestEntity(1, "Alpha"),
                new TestEntity(2, "Beta")
            };

            var fallback = ListSelectionHelper.GetFallbackItemAfterRemoval(items, item => item.Id == 3);

            Assert.Null(fallback);
        }

        [Fact]
        public void GetFallbackItemAfterRemoval_ShouldRequirePredicate()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ListSelectionHelper.GetFallbackItemAfterRemoval<TestEntity>([], null));
        }

        private sealed class TestEntity
        {
            public TestEntity(int id, string name)
            {
                Id = id;
                Name = name;
            }

            public int Id { get; }
            public string Name { get; }
        }
    }
}
