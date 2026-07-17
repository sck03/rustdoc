namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowDraftMetadataHelper
    {
        public const string GeneratedStatus = "Generated";
        public const string NotGeneratedDisplay = "未生成";

        public static string ResolveDisplayStatus(string status, DateTime lastGeneratedAt, int draftRevision)
        {
            string normalizedStatus = Normalize(status);
            if (string.IsNullOrWhiteSpace(normalizedStatus))
            {
                return NotGeneratedDisplay;
            }

            return string.Equals(normalizedStatus, GeneratedStatus, StringComparison.Ordinal) &&
                   lastGeneratedAt <= DateTime.MinValue &&
                   draftRevision <= 0
                ? NotGeneratedDisplay
                : normalizedStatus;
        }

        public static string ResolveStatusForSave(string status)
        {
            string normalizedStatus = Normalize(status);
            return string.IsNullOrWhiteSpace(normalizedStatus) ||
                   string.Equals(normalizedStatus, NotGeneratedDisplay, StringComparison.Ordinal)
                ? GeneratedStatus
                : normalizedStatus;
        }

        public static string FormatLastGeneratedAt(DateTime lastGeneratedAt)
        {
            return lastGeneratedAt > DateTime.MinValue
                ? lastGeneratedAt.ToString("yyyy-MM-dd HH:mm")
                : string.Empty;
        }

        public static int CountWarnings(string warningSummary)
        {
            string normalizedSummary = Normalize(warningSummary);
            if (string.IsNullOrWhiteSpace(normalizedSummary))
            {
                return 0;
            }

            return normalizedSummary
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
