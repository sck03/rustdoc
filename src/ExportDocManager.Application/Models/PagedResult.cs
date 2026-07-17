namespace ExportDocManager.Models
{
    /// <summary>
    /// Represents a paginated list of items.
    /// 代表一个分页的项目列表。
    /// </summary>
    /// <typeparam name="T">The type of the items in the list.</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// The items for the current page.
        /// 当前页的项目。
        /// </summary>
        public List<T> Items { get; set; }

        /// <summary>
        /// The total number of items across all pages.
        /// 所有页面的项目总数。
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// The current page number.
        /// 当前页码。
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// The number of items per page.
        /// 每页的项目数。
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// The total number of pages.
        /// 总页数。
        /// </summary>
        public int TotalPages => PagingHelper.CalculateTotalPages(TotalCount, PageSize);

        /// <summary>
        /// Indicates if there is a previous page.
        /// 指示是否存在上一页。
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// Indicates if there is a next page.
        /// 指示是否存在下一页。
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;

        public PagedResult(List<T> items, int totalCount, int pageNumber, int pageSize)
        {
            Items = items;
            TotalCount = totalCount;
            PageNumber = pageNumber;
            PageSize = pageSize;
        }
    }
}
