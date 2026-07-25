using System;
using System.Collections.Generic;

namespace ExportDocManager.Models.Entities
{
    public static class InvoiceTypeCatalog
    {
        public const string Actual = "实际数据";
        public const string Customs = "报关数据";

        public static readonly IReadOnlyList<string> Types = [Actual, Customs];

        public static bool IsKnown(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return Types.Contains(normalized, StringComparer.Ordinal);
        }

        public static string Normalize(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(normalized) ? Actual : normalized;
        }
    }
}
