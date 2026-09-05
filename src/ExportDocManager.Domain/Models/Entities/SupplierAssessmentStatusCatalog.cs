namespace ExportDocManager.Models.Entities;

public static class SupplierAssessmentStatusCatalog
{
    public const string Draft = "Draft";
    public const string Confirmed = "Confirmed";

    public static readonly IReadOnlyList<string> Values = [Draft, Confirmed];

    public static bool IsKnown(string? value) =>
        Values.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value) =>
        Values.FirstOrDefault(item => string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? string.Empty;
}
