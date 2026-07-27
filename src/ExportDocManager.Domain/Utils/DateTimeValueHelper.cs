namespace ExportDocManager.Utils
{
    public static class DateTimeValueHelper
    {
        public static readonly DateTime WinFormsMinDate = new(1753, 1, 1);

        public static DateTime NormalizeBusinessDate(DateTime value, DateTime? fallback = null)
        {
            if (value != default && value >= WinFormsMinDate)
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }

            var fallbackValue = fallback ?? DateTime.Today;
            return DateTime.SpecifyKind(
                fallbackValue >= WinFormsMinDate ? fallbackValue : WinFormsMinDate,
                DateTimeKind.Utc);
        }

        /// <summary>
        /// Normalizes a timestamp before it is persisted to PostgreSQL's
        /// <c>timestamp with time zone</c> columns.  Values coming from a
        /// date-only form are intentionally treated as UTC without changing
        /// their calendar fields; local timestamps preserve their instant.
        /// </summary>
        public static DateTime NormalizeUtcTimestamp(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
