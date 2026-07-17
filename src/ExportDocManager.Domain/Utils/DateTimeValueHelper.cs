namespace ExportDocManager.Utils
{
    public static class DateTimeValueHelper
    {
        public static readonly DateTime WinFormsMinDate = new(1753, 1, 1);

        public static DateTime NormalizeBusinessDate(DateTime value, DateTime? fallback = null)
        {
            if (value != default && value >= WinFormsMinDate)
            {
                return value;
            }

            var fallbackValue = fallback ?? DateTime.Today;
            return fallbackValue >= WinFormsMinDate ? fallbackValue : WinFormsMinDate;
        }
    }
}
