namespace ExportDocManager.Models
{
    public static class PagingHelper
    {
        public static int CalculateTotalPages(int totalCount, int pageSize)
        {
            return pageSize <= 0
                ? 0
                : (int)Math.Ceiling(Math.Max(0, totalCount) / (double)pageSize);
        }

        public static int CalculateOffset(int pageNumber, int pageSize)
        {
            if (pageNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "页码必须大于或等于 1。");
            }
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize), "每页数量必须大于或等于 1。");
            }

            long offset = ((long)pageNumber - 1L) * pageSize;
            if (offset > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber), "页码超出当前查询支持的范围。");
            }

            return (int)offset;
        }

        public static PagedResult<T> CreateLocalPage<T>(IReadOnlyList<T> items, int pageNumber, int pageSize)
        {
            var normalizedItems = items ?? [];
            var normalizedPageSize = Math.Max(1, pageSize);
            var totalCount = normalizedItems.Count;
            var totalPages = CalculateTotalPages(totalCount, normalizedPageSize);
            var normalizedPageNumber = totalPages <= 0
                ? 1
                : Math.Clamp(Math.Max(1, pageNumber), 1, totalPages);
            var pagedItems = normalizedItems
                .Skip(CalculateOffset(normalizedPageNumber, normalizedPageSize))
                .Take(normalizedPageSize)
                .ToList();

            return new PagedResult<T>(pagedItems, totalCount, normalizedPageNumber, normalizedPageSize);
        }
    }
}
