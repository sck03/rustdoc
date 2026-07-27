namespace ExportDocManager.Models
{
    public static class UpdaterEndpointPolicy
    {
        public const int MaxLength = 2048;

        public static string Normalize(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        public static bool TryValidate(string value, out Uri endpoint, out string errorMessage)
        {
            endpoint = null;
            errorMessage = string.Empty;

            string normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                return true;
            }

            if (normalized.Length > MaxLength)
            {
                errorMessage = $"软件更新地址不能超过 {MaxLength} 个字符。";
                return false;
            }

            if (normalized.Any(char.IsControl) || normalized.Contains('\\'))
            {
                errorMessage = "软件更新地址包含不允许的控制字符或反斜杠。";
                return false;
            }

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(parsed.Host))
            {
                errorMessage = "软件更新地址必须是完整的 http:// 或 https:// 绝对地址。";
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.UserInfo))
            {
                errorMessage = "软件更新地址不能包含用户名或密码；需要鉴权时应由受控更新网关处理。";
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.Fragment))
            {
                errorMessage = "软件更新地址不能包含 # 片段。";
                return false;
            }

            endpoint = parsed;
            return true;
        }

        public static bool UsesInsecureHttp(string value)
        {
            return TryValidate(value, out var endpoint, out _) &&
                   endpoint != null &&
                   string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
