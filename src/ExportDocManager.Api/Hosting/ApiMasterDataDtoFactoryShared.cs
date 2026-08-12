namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static ApiPagedResponse<TDto> FromPage<TEntity, TDto>(
            ExportDocManager.Models.PagedResult<TEntity> page,
            Func<IEnumerable<TEntity>, IReadOnlyList<TDto>> mapItems)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(mapItems);
            return new ApiPagedResponse<TDto>(
                mapItems(page.Items),
                page.TotalCount,
                page.PageNumber,
                page.PageSize,
                page.TotalPages,
                page.HasPreviousPage,
                page.HasNextPage);
        }

        private static string RowVersionToString(byte[]? rowVersion)
        {
            return rowVersion == null || rowVersion.Length == 0
                ? string.Empty
                : Convert.ToBase64String(rowVersion);
        }

        private static byte[]? RowVersionFromString(string rowVersion)
        {
            return string.IsNullOrWhiteSpace(rowVersion)
                ? null
                : Convert.FromBase64String(rowVersion);
        }
    }
}
