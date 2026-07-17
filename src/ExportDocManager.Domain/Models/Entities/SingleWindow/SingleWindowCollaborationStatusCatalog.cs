namespace ExportDocManager.Models.Entities
{
    public static class SingleWindowCollaborationStatusCatalog
    {
        public const string Pending = "Pending";
        public const string Assigned = "Assigned";
        public const string Submitted = "Submitted";
        public const string Completed = "Completed";
        public const string Failed = "Failed";

        private static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Pending] = "待处理",
                [Assigned] = "已指派",
                [Submitted] = "已提交",
                [Completed] = "已完成",
                [Failed] = "失败"
            };

        public static IReadOnlyList<string> AllStatuses { get; } =
        [
            Pending,
            Assigned,
            Submitted,
            Completed,
            Failed
        ];

        public static string Normalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            var trimmed = status.Trim();
            return AllStatuses.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
                ?? trimmed;
        }

        public static string GetDisplayName(string status)
        {
            var normalized = Normalize(status);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return DisplayNames.TryGetValue(normalized, out var displayName)
                ? displayName
                : normalized;
        }
    }
}
