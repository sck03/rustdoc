using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Services.Infrastructure;

internal static partial class DiagnosticLogSanitizer
{
    private const string Redacted = "[REDACTED]";

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string sanitized = HttpUrlRegex().Replace(value, SanitizeUrl);
        sanitized = BearerTokenRegex().Replace(sanitized, $"Bearer {Redacted}");
        sanitized = SensitiveAssignmentRegex().Replace(
            sanitized,
            match => match.Groups["prefix"].Value + Redacted);
        sanitized = EmailRegex().Replace(sanitized, "[REDACTED_EMAIL]");
        sanitized = WindowsUserPathRegex().Replace(sanitized, "$1\\[REDACTED]");
        sanitized = UnixHomePathRegex().Replace(sanitized, "$1/[REDACTED]");
        return RemoveUnsafeControlCharacters(sanitized);
    }

    public static byte[] SanitizeUtf8(ReadOnlySpan<byte> input, int maximumOutputBytes)
    {
        if (input.IsEmpty || maximumOutputBytes <= 0)
        {
            return [];
        }

        string sanitized = Sanitize(Encoding.UTF8.GetString(input));
        byte[] output = new byte[maximumOutputBytes];
        Encoding.UTF8.GetEncoder().Convert(
            sanitized.AsSpan(),
            output.AsSpan(),
            flush: true,
            out _,
            out int bytesUsed,
            out _);
        return output.AsSpan(0, bytesUsed).ToArray();
    }

    private static string SanitizeUrl(Match match)
    {
        if (!Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "[REDACTED_URL]";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
            Path = RedactSensitivePathSegments(uri.AbsolutePath)
        };
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }

    private static string RedactSensitivePathSegments(string path)
    {
        string[] segments = path.Split('/', StringSplitOptions.None);
        for (int index = 0; index < segments.Length; index++)
        {
            string decoded = Uri.UnescapeDataString(segments[index]);
            if (decoded.Length >= 24 && decoded.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            {
                segments[index] = Redacted;
            }
        }

        return string.Join('/', segments);
    }

    private static string RemoveUnsafeControlCharacters(string value)
    {
        var output = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            output.Append(!char.IsControl(ch) || ch is '\r' or '\n' or '\t' ? ch : ' ');
        }
        return output.ToString();
    }

    [GeneratedRegex("""https?://[^\s"'<>]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("""(?<prefix>["']?(?:password|passwd|pwd|secret|api[-_]?key|token|credential|access[-_]?key|signing[-_]?key|encryption[-_]?key|connection[-_]?string)["']?\s*(?:=|:)\s*["']?)[^"'\s,;}\]]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("""\b([A-Z]:\\Users)\\[^\\\s"'<>|]+""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex("""(?<![A-Za-z0-9_])(/(?:Users|home))/[^/\s"'<>]+""", RegexOptions.CultureInvariant)]
    private static partial Regex UnixHomePathRegex();
}
