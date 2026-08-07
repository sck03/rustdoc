using System.Collections.Frozen;

namespace ExportDocManager.Utils
{
    public static class MailServerHelper
    {
        private static readonly FrozenDictionary<string, (string SmtpHost, int Port, bool Ssl)> CommonMailServers =
            new Dictionary<string, (string, int, bool)>
            {
                { "qq.com", ("smtp.qq.com", 465, true) },
                { "vip.qq.com", ("smtp.qq.com", 465, true) },
                { "foxmail.com", ("smtp.qq.com", 465, true) },
                { "163.com", ("smtp.163.com", 465, true) },
                { "126.com", ("smtp.126.com", 465, true) },
                { "yeah.net", ("smtp.yeah.net", 465, true) },
                { "sina.com", ("smtp.sina.com", 465, true) },
                { "sina.cn", ("smtp.sina.cn", 465, true) },
                { "sohu.com", ("smtp.sohu.com", 465, true) },
                { "gmail.com", ("smtp.gmail.com", 587, true) },
                { "outlook.com", ("smtp.office365.com", 587, true) },
                { "hotmail.com", ("smtp.office365.com", 587, true) },
                { "live.com", ("smtp.office365.com", 587, true) },
                { "yahoo.com", ("smtp.mail.yahoo.com", 465, true) },
                { "aliyun.com", ("smtp.aliyun.com", 465, true) },
                { "139.com", ("smtp.139.com", 465, true) },
                { "wo.cn", ("smtp.wo.cn", 465, true) },
                { "189.cn", ("smtp.189.cn", 465, true) },
                { "bridgegroup.cn", ("smtpcom.263xmail.com", 465, true) }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static (string SmtpHost, int Port, bool Ssl)? GetServerConfig(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            ReadOnlySpan<char> normalizedEmail = email.AsSpan().Trim();
            int separatorIndex = normalizedEmail.IndexOf('@');
            if (separatorIndex <= 0 ||
                separatorIndex != normalizedEmail.LastIndexOf('@') ||
                separatorIndex == normalizedEmail.Length - 1)
            {
                return null;
            }

            string domain = normalizedEmail[(separatorIndex + 1)..].Trim().ToString();
            if (CommonMailServers.TryGetValue(domain, out var config))
            {
                return config;
            }

            if (domain.EndsWith("263.net", StringComparison.OrdinalIgnoreCase) ||
                domain.EndsWith("263.net.cn", StringComparison.OrdinalIgnoreCase))
            {
                return ("smtp.263.net", 465, true);
            }

            if (domain.EndsWith("263xmail.com", StringComparison.OrdinalIgnoreCase))
            {
                return ("smtpcom.263xmail.com", 465, true);
            }

            return ($"smtp.{domain.ToLowerInvariant()}", 465, true);
        }
    }
}
