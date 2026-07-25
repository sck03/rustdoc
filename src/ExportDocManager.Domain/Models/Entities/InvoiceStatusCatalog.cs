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

        private static readonly HashSet<string> LockedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Verified,
            Shipped,
            Completed,
            Cancelled
        };

        public static readonly IReadOnlyList<string> Statuses =
        [
            Draft,
            Verified,
            Shipped,
            Completed,
            Cancelled
        ];

        public static bool IsKnown(string status)
        {
            string normalized = Normalize(status);
            return Statuses.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }

        public static string Normalize(string status)
        {
            string normalized = status?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Draft;
            }

            return Statuses.FirstOrDefault(item =>
                       string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? normalized;
        }

        public static bool IsEditable(string status)
        {
            return string.Equals(Normalize(status), Draft, StringComparison.OrdinalIgnoreCase);
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

        public static bool CanTransition(string currentStatus, string targetStatus)
        {
            string current = Normalize(currentStatus);
            string target = Normalize(targetStatus);
            if (!IsKnown(current) || !IsKnown(target) ||
                string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(target, Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                return !string.Equals(current, Cancelled, StringComparison.OrdinalIgnoreCase);
            }

            return current switch
            {
                Draft => string.Equals(target, Verified, StringComparison.OrdinalIgnoreCase),
                Verified => string.Equals(target, Shipped, StringComparison.OrdinalIgnoreCase),
                Shipped => string.Equals(target, Completed, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        public static string GetNextOperationalStatus(string status)
        {
            return Normalize(status) switch
            {
                Draft => Verified,
                Verified => Shipped,
                Shipped => Completed,
                _ => string.Empty
            };
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
