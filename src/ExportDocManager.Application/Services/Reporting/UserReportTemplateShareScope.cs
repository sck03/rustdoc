namespace ExportDocManager.Services.Reporting
{
    public static class UserReportTemplateShareScope
    {
        public const string Private = "Private";
        public const string Department = "Department";
        public const string Company = "Company";
        public const string All = "All";

        public static readonly IReadOnlyList<string> Values = [Private, Department, Company, All];

        public static bool IsKnown(string value) =>
            Values.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string value) =>
            IsKnown(value)
                ? Values.First(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase))
                : Private;

        public static string ToDisplayName(string value) => Normalize(value) switch
        {
            Department => "同部门可见",
            Company => "同公司可见",
            All => "团队成员可见",
            _ => "仅自己可见"
        };
    }
}
