using ExportDocManager.Models;

namespace ExportDocManager.Application.Tests
{
    public class PaginationHelperTests
    {
        [Fact]
        public void CreateLocalPage_ShouldClampOverflowPageNumber()
        {
            var page = PagingHelper.CreateLocalPage([1, 2, 3], pageNumber: 5, pageSize: 2);

            Assert.Equal(2, page.PageNumber);
            Assert.Equal(2, page.TotalPages);
            Assert.Equal([3], page.Items);
        }

        [Fact]
        public void CreateLocalPage_ShouldKeepFirstPageForEmptyResults()
        {
            var page = PagingHelper.CreateLocalPage<int>([], pageNumber: 3, pageSize: 20);

            Assert.Equal(1, page.PageNumber);
            Assert.Equal(0, page.TotalPages);
            Assert.Empty(page.Items);
        }

        [Fact]
        public void CalculateOffset_ShouldRejectIntegerOverflow()
        {
            Assert.Equal(0, PagingHelper.CalculateOffset(1, 200));
            Assert.Equal(200, PagingHelper.CalculateOffset(2, 200));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PagingHelper.CalculateOffset(int.MaxValue, 200));
        }
    }
}
