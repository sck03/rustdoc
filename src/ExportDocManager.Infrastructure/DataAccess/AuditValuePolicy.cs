using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ExportDocManager.DataAccess;

internal static class AuditValuePolicy
{
    private static readonly HashSet<string> SafeTextProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Status", "Type", "Category", "Currency", "CurrencyCode", "Unit", "UnitCode",
        "AccessLevel", "Role", "Action", "Provider", "Mode", "Source", "ReportType",
        "InvoiceType", "CalculationMode"
    };

    public static object? Sanitize(string propertyName, object? value)
    {
        if (value == null) return null;
        string name = propertyName ?? string.Empty;
        if (IsSecret(name)) return "[REDACTED]";
        if (value is string text)
        {
            return SafeTextProperties.Contains(name) || text.Length == 0
                ? text
                : DescribeText(text);
        }
        if (value is byte[] bytes) return DescribeBytes(bytes);
        if (value.GetType().IsEnum || value is bool or char or Guid or
            DateOnly or TimeOnly or DateTime or DateTimeOffset or TimeSpan or
            byte or sbyte or short or ushort or int or uint or long or ulong or
            float or double or decimal)
        {
            return value;
        }

        return $"[VALUE type={value.GetType().Name}]";
    }

    private static bool IsSecret(string name) =>
        name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase);

    private static string DescribeText(string value) =>
        $"[TEXT length={value.Length.ToString(CultureInfo.InvariantCulture)} sha256={Hash(Encoding.UTF8.GetBytes(value))}]";

    private static string DescribeBytes(byte[] value) =>
        $"[BINARY length={value.Length.ToString(CultureInfo.InvariantCulture)} sha256={Hash(value)}]";

    private static string Hash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value))[..16];
}
