namespace ExportDocManager.Models.Entities;

/// <summary>
/// Canonical CRM states shared by API validation, imports and the web client.
/// Keeping the values in one catalog prevents slightly different spellings
/// from creating unqueryable records.
/// </summary>
public static class CrmCustomerStatusCatalog
{
    public const string Prospect = "潜在客户";
    public const string InProgress = "跟进中";
    public const string Won = "已成交";
    public const string Paused = "暂停";
    public const string Lost = "已流失";

    public static readonly IReadOnlyList<string> Values =
        [Prospect, InProgress, Won, Paused, Lost];

    public static bool IsKnown(string? value) =>
        Values.Contains(value?.Trim() ?? string.Empty, StringComparer.Ordinal);

    public static string Normalize(string? value, string fallback = Prospect)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return fallback;
        }

        return Values.FirstOrDefault(item =>
                   string.Equals(item, normalized, StringComparison.Ordinal))
               ?? throw new ArgumentException("CRM 客户状态无效。");
    }
}

public static class CrmFollowUpTypeCatalog
{
    public const string Email = "邮件";
    public const string Phone = "电话";
    public const string Meeting = "拜访";
    public const string Quotation = "报价";
    public const string Other = "其他";

    public static readonly IReadOnlyList<string> Values =
        [Email, Phone, Meeting, Quotation, Other];

    public static bool IsKnown(string? value) =>
        Values.Contains(value?.Trim() ?? string.Empty, StringComparer.Ordinal);

    public static string Normalize(string? value, string fallback = Other)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return fallback;
        }

        return Values.FirstOrDefault(item =>
                   string.Equals(item, normalized, StringComparison.Ordinal))
               ?? throw new ArgumentException("跟进方式无效。");
    }
}
