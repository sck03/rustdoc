namespace ExportDocManager.Models.Entities
{
    public static class TemplateLifecycleStatusCatalog
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
        public const string Disabled = "Disabled";
        public const string Archived = "Archived";

        public static readonly IReadOnlyList<string> Values = [Draft, Published, Disabled, Archived];

        public static bool IsKnown(string? value) =>
            Values.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string? value) =>
            Values.FirstOrDefault(item => string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
    }

    public static class TemplateShareScopeCatalog
    {
        public const string Private = "Private";
        public const string Department = "Department";
        public const string Company = "Company";
        public const string All = "All";

        public static readonly IReadOnlyList<string> Values = [Private, Department, Company, All];

        public static bool IsKnown(string? value) =>
            Values.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string? value) =>
            Values.FirstOrDefault(item => string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;

        public static string ToDisplayName(string? value) => Normalize(value) switch
        {
            Department => "同部门可见",
            Company => "同公司可见",
            All => "全部成员可见",
            _ => "仅自己可见"
        };
    }
}
