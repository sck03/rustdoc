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
                .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToList();

            return new PagedResult<T>(pagedItems, totalCount, normalizedPageNumber, normalizedPageSize);
        }
    }
}
