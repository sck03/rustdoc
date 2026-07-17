using System;
using System.Collections.Generic;

namespace ExportDocManager.Models.Entities
{
    public static class InvoiceStatusCatalog
    {
        public const string Draft = "Draft";
        public const string Verified = "Verified";
        public const string Shipped = "Shipped";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";

        private static readonly IReadOnlyDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Draft] = "草稿",
                [Verified] = "已核对",
                [Shipped] = "已出运",
                [Completed] = "已结汇",
                [Cancelled] = "已作废"
            };

        private static readonly HashSet<string> LockedStatuses =
        [
            Verified,
            Shipped,
            Completed,
            Cancelled
        ];

        public static bool IsEditable(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return true;
            }

            return !LockedStatuses.Contains(status.Trim());
        }

        public static bool CanUnverify(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return LockedStatuses.Contains(status.Trim());
        }

        public static bool IsCancelled(string status)
        {
            return string.Equals(status?.Trim(), Cancelled, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetDisplayName(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            return DisplayNames.TryGetValue(status.Trim(), out var displayName)
                ? displayName
                : status.Trim();
        }
    }
}
