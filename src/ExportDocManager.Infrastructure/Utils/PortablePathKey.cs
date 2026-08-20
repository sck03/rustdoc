using System.Text;

namespace ExportDocManager.Utils
{
    /// <summary>Provides host-independent NFC and case-folded portable path identity.</summary>
    public static class PortablePathKey
    {
        public static IEqualityComparer<string> Comparer { get; } = new PortablePathComparer();

        public static string NormalizeRelativePath(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            string raw = value.Trim();
            if (raw.StartsWith('/') || raw.StartsWith('\\') || Path.IsPathRooted(raw))
            {
                throw new InvalidDataException("可移植路径不能使用绝对路径。");
            }

            string normalized = NormalizeForComparison(raw);
            string[] segments = normalized.Split('/');
            if (segments.Length == 0 || segments.Any(segment =>
                    string.IsNullOrWhiteSpace(segment) ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal) ||
                    !CrossPlatformFileNamePolicy.IsSafeFileName(segment)))
            {
                throw new InvalidDataException("可移植路径包含无效文件名或目录名。");
            }

            return string.Join('/', segments.Select(segment =>
                segment.Normalize(NormalizationForm.FormC)));
        }

        private static string NormalizeForComparison(string value) =>
            (value ?? string.Empty)
                .Normalize(NormalizationForm.FormC)
                .Replace('\\', '/')
                .Trim('/');

        private sealed class PortablePathComparer : IEqualityComparer<string>
        {
            public bool Equals(string? x, string? y) =>
                string.Equals(
                    NormalizeForComparison(x ?? string.Empty),
                    NormalizeForComparison(y ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(string obj) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(
                    NormalizeForComparison(obj ?? string.Empty));
        }
    }
}
