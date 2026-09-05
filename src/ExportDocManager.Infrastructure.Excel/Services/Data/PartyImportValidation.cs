using System.Net.Mail;
using System.Text;

namespace ExportDocManager.Services.Data;

/// <summary>
/// Shared server-side normalization for CRM and supplier imports. Preview data
/// is treated as an untrusted hint; the same rules are applied again during the
/// actual import so a caller cannot forge IsDuplicate/Error flags.
/// </summary>
internal static class PartyImportValidation
{
    public static string Text(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<string> errors)
    {
        string normalized = (value ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Trim();
        if (normalized.Length > maximumLength)
        {
            errors.Add($"{fieldName}不能超过 {maximumLength} 个字符。");
        }

        return normalized;
    }

    public static string Required(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<string> errors)
    {
        string normalized = Text(value, maximumLength, fieldName, errors);
        if (normalized.Length == 0)
        {
            errors.Add($"{fieldName}不能为空。");
        }

        return normalized;
    }

    public static string Email(string? value, ICollection<string> errors)
    {
        string normalized = Text(value, 200, "联系人邮箱", errors);
        if (normalized.Length == 0) return normalized;

        try
        {
            var address = new MailAddress(normalized);
            if (!string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("联系人邮箱格式无效。");
            }
        }
        catch (FormatException)
        {
            errors.Add("联系人邮箱格式无效。");
        }

        return normalized;
    }

    public static string CanonicalKey(string? value) =>
        (value ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToUpperInvariant();

    public static string JoinErrors(IEnumerable<string> errors) =>
        string.Join(" ", errors.Where(error => !string.IsNullOrWhiteSpace(error)).Distinct(StringComparer.Ordinal));
}
