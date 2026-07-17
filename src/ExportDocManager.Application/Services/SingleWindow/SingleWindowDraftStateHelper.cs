using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed record SingleWindowLockedFieldDetail(
        string Key,
        string DisplayName,
        string CurrentValue,
        string SuggestedValue);

    public static partial class SingleWindowDraftStateHelper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };
        private const int SourceDiffSummaryLineLimit = 20;

        public static int CountLockedFields(string lockedFieldsJson) => ParseLockedFields(lockedFieldsJson).Count;

        public static HashSet<string> ParseLockedFields(string lockedFieldsJson)
        {
            if (string.IsNullOrWhiteSpace(lockedFieldsJson))
            {
                return [];
            }

            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(lockedFieldsJson, JsonOptions) ?? [];
                return values
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToHashSet(StringComparer.Ordinal);
            }
            catch
            {
                return [];
            }
        }

        public static string SerializeLockedFields(IEnumerable<string> keys)
        {
            var normalized = (keys ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList();

            return normalized.Count == 0
                ? string.Empty
                : JsonSerializer.Serialize(normalized, JsonOptions);
        }

        public static string ComputeBaselineHash(string baselineJson)
        {
            if (string.IsNullOrWhiteSpace(baselineJson))
            {
                return string.Empty;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(baselineJson));
            return Convert.ToHexString(bytes);
        }

        public static CustomsCooDocument BuildCustomsCooLockedOverlay(
            CustomsCooDocument stored,
            IReadOnlyList<Item> sourceItems)
        {
            if (stored == null)
            {
                return null;
            }

            var overlay = SingleWindowSourceCloneHelper.CloneCustomsCooDocument(stored);
            var lockedKeys = ParseLockedFields(stored.ManualLockedFieldsJson);
            lockedKeys.UnionWith(InferCustomsCooLockedFieldsFromBaseline(stored));
            var sourceIdentities = BuildSourceItemIdentitySet(sourceItems);

            ClearUnlockedStringProperties(
                overlay,
                lockedKeys,
                CustomsCooDocumentEditableExclusions);

            foreach (var row in overlay.Items ?? [])
            {
                string identity = BuildGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo);
                if (!sourceIdentities.Contains(identity))
                {
                    continue;
                }

                ClearUnlockedGoodsRowProperties(row, lockedKeys);
            }

            return overlay;
        }

        public static AgentConsignmentDocument BuildAgentConsignmentLockedOverlay(AgentConsignmentDocument stored)
        {
            if (stored == null)
            {
                return null;
            }

            var overlay = SingleWindowSourceCloneHelper.CloneAgentConsignmentDocument(stored);
            var lockedKeys = ParseLockedFields(stored.ManualLockedFieldsJson);
            ClearUnlockedStringProperties(
                overlay,
                lockedKeys,
                AgentConsignmentEditableExclusions);
            return overlay;
        }

        public static HashSet<string> BuildCustomsCooLockedFields(CustomsCooDocument current, CustomsCooDocument defaults)
        {
            var lockedKeys = new HashSet<string>(StringComparer.Ordinal);
            if (current == null)
            {
                return lockedKeys;
            }

            AddChangedStringPropertyKeys(
                current,
                defaults ?? new CustomsCooDocument(),
                lockedKeys,
                CustomsCooDocumentEditableExclusions);

            var defaultRows = (defaults?.Items ?? [])
                .ToDictionary(
                    item => BuildGoodsIdentity(item.SourceItemId, item.SourceStyleNo, item.GNo),
                    item => item,
                    StringComparer.Ordinal);

            foreach (var row in current.Items ?? [])
            {
                string identity = BuildGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo);
                defaultRows.TryGetValue(identity, out var defaultRow);
                AddChangedGoodsRowKeys(row, defaultRow, lockedKeys);
            }

            return lockedKeys;
        }

        public static HashSet<string> BuildAgentConsignmentLockedFields(AgentConsignmentDocument current, AgentConsignmentDocument defaults)
        {
            var lockedKeys = new HashSet<string>(StringComparer.Ordinal);
            if (current == null)
            {
                return lockedKeys;
            }

            AddChangedStringPropertyKeys(
                current,
                defaults ?? new AgentConsignmentDocument(),
                lockedKeys,
                AgentConsignmentEditableExclusions);

            return lockedKeys;
        }

        public static string BuildCustomsCooSourceBaselineJson(CustomsCooDocument defaults) =>
            SerializeBaselineMap(BuildCustomsCooSourceDiffBaselineMap(defaults));

        public static string BuildAgentConsignmentSourceBaselineJson(AgentConsignmentDocument defaults) =>
            SerializeBaselineMap(BuildAgentConsignmentBaselineMap(defaults));

        public static (int Count, string Summary) BuildCustomsCooSourceDiff(string baselineJson, CustomsCooDocument currentDefaults) =>
            BuildSourceDiffSummary(DeserializeBaselineMap(baselineJson), BuildCustomsCooSourceDiffBaselineMap(currentDefaults));

        public static (int Count, string Summary) BuildAgentConsignmentSourceDiff(string baselineJson, AgentConsignmentDocument currentDefaults) =>
            BuildSourceDiffSummary(DeserializeBaselineMap(baselineJson), BuildAgentConsignmentBaselineMap(currentDefaults));

        public static IReadOnlyList<SingleWindowLockedFieldDetail> DescribeCustomsCooLockedFields(
            CustomsCooDocument current,
            CustomsCooDocument defaults)
        {
            var lockedKeys = BuildCustomsCooLockedFields(current, defaults);
            return BuildLockedFieldDetails(
                lockedKeys,
                BuildCustomsCooBaselineMap(current),
                BuildCustomsCooBaselineMap(defaults));
        }

        public static IReadOnlyList<SingleWindowLockedFieldDetail> DescribeAgentConsignmentLockedFields(
            AgentConsignmentDocument current,
            AgentConsignmentDocument defaults)
        {
            var lockedKeys = BuildAgentConsignmentLockedFields(current, defaults);
            return BuildLockedFieldDetails(
                lockedKeys,
                BuildAgentConsignmentBaselineMap(current),
                BuildAgentConsignmentBaselineMap(defaults));
        }

        public static string DescribeFieldKey(string key) => FormatFieldKey(key);

        public static string GetGoodsIdentity(int sourceItemId, string sourceStyleNo, int lineNo) =>
            BuildGoodsIdentity(sourceItemId, sourceStyleNo, lineNo);

        public static bool TryParseGoodsFieldKey(string key, out string identity, out string propertyName)
        {
            identity = string.Empty;
            propertyName = string.Empty;
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith("Goods:", StringComparison.Ordinal))
            {
                return false;
            }

            string goodsPayload = key["Goods:".Length..];
            int lastSeparatorIndex = goodsPayload.LastIndexOf(':');
            if (lastSeparatorIndex <= 0 || lastSeparatorIndex >= goodsPayload.Length - 1)
            {
                return false;
            }

            identity = goodsPayload[..lastSeparatorIndex];
            propertyName = goodsPayload[(lastSeparatorIndex + 1)..];
            return !string.IsNullOrWhiteSpace(identity) && !string.IsNullOrWhiteSpace(propertyName);
        }

        private static void ClearUnlockedStringProperties<T>(
            T target,
            IReadOnlySet<string> lockedKeys,
            IReadOnlySet<string> exclusions)
        {
            foreach (var property in GetStringProperties(typeof(T), exclusions))
            {
                if (lockedKeys.Contains(property.Name))
                {
                    continue;
                }

                property.SetValue(target, string.Empty);
            }
        }

        private static void ClearUnlockedGoodsRowProperties(CustomsCooItem target, IReadOnlySet<string> lockedKeys)
        {
            string identity = BuildGoodsIdentity(target.SourceItemId, target.SourceStyleNo, target.GNo);
            foreach (var property in GetStringProperties(typeof(CustomsCooItem), CustomsCooItemEditableExclusions))
            {
                string key = BuildGoodsFieldKey(identity, property.Name);
                if (lockedKeys.Contains(key))
                {
                    continue;
                }

                property.SetValue(target, string.Empty);
            }
        }

        private static void AddChangedStringPropertyKeys<T>(
            T current,
            T defaults,
            ISet<string> lockedKeys,
            IReadOnlySet<string> exclusions)
        {
            foreach (var property in GetStringProperties(typeof(T), exclusions))
            {
                string currentValue = NormalizeText(property.GetValue(current) as string);
                string defaultValue = NormalizeText(property.GetValue(defaults) as string);
                if (!string.Equals(currentValue, defaultValue, StringComparison.Ordinal))
                {
                    lockedKeys.Add(property.Name);
                }
            }
        }

        private static void AddChangedGoodsRowKeys(
            CustomsCooItem current,
            CustomsCooItem defaults,
            ISet<string> lockedKeys)
        {
            string identity = BuildGoodsIdentity(current.SourceItemId, current.SourceStyleNo, current.GNo);
            defaults ??= new CustomsCooItem
            {
                SourceItemId = current.SourceItemId,
                SourceStyleNo = current.SourceStyleNo,
                GNo = current.GNo
            };

            foreach (var property in GetStringProperties(typeof(CustomsCooItem), CustomsCooItemEditableExclusions))
            {
                string currentValue = NormalizeText(property.GetValue(current) as string);
                string defaultValue = NormalizeText(property.GetValue(defaults) as string);
                if (!string.Equals(currentValue, defaultValue, StringComparison.Ordinal))
                {
                    lockedKeys.Add(BuildGoodsFieldKey(identity, property.Name));
                }
            }
        }

        private static IReadOnlyList<SingleWindowLockedFieldDetail> BuildLockedFieldDetails(
            IEnumerable<string> lockedKeys,
            IReadOnlyDictionary<string, string> currentMap,
            IReadOnlyDictionary<string, string> defaultMap)
        {
            return (lockedKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => new SingleWindowLockedFieldDetail(
                    key,
                    FormatFieldKey(key),
                    currentMap.TryGetValue(key, out var currentValue) ? currentValue : string.Empty,
                    defaultMap.TryGetValue(key, out var defaultValue) ? defaultValue : string.Empty))
                .ToList();
        }

        private static IEnumerable<PropertyInfo> GetStringProperties(Type type, IReadOnlySet<string> exclusions)
        {
            return type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property =>
                    property.PropertyType == typeof(string) &&
                    property.CanRead &&
                    property.CanWrite &&
                    !exclusions.Contains(property.Name));
        }

        private static HashSet<string> BuildSourceItemIdentitySet(IReadOnlyList<Item> sourceItems)
        {
            return (sourceItems ?? [])
                .Where(item => item != null)
                .Select((item, index) => BuildGoodsIdentity(item.Id, item.StyleNo, index + 1))
                .ToHashSet(StringComparer.Ordinal);
        }

        private static string BuildGoodsIdentity(int sourceItemId, string sourceStyleNo, int lineNo)
        {
            if (sourceItemId > 0)
            {
                return $"Item:{sourceItemId}";
            }

            string styleNo = NormalizeText(sourceStyleNo);
            if (!string.IsNullOrWhiteSpace(styleNo))
            {
                return $"Style:{styleNo}";
            }

            return $"Row:{Math.Max(1, lineNo)}";
        }

        private static string BuildGoodsFieldKey(string identity, string propertyName) => $"Goods:{identity}:{propertyName}";

        private static string FormatFieldKey(string key)
        {
            if (!key.StartsWith("Goods:", StringComparison.Ordinal))
            {
                return PropertyDisplayNames.TryGetValue(key, out var displayName)
                    ? displayName
                    : key;
            }

            string goodsPayload = key["Goods:".Length..];
            int lastSeparatorIndex = goodsPayload.LastIndexOf(':');
            if (lastSeparatorIndex <= 0 || lastSeparatorIndex >= goodsPayload.Length - 1)
            {
                return key;
            }

            string identity = goodsPayload[..lastSeparatorIndex];
            string propertyName = goodsPayload[(lastSeparatorIndex + 1)..];
            string label = PropertyDisplayNames.TryGetValue(propertyName, out var goodsDisplayName)
                ? goodsDisplayName
                : propertyName;

            string rowLabel = identity.StartsWith("Item:", StringComparison.Ordinal)
                ? $"货项{identity["Item:".Length..]}"
                : identity.StartsWith("Style:", StringComparison.Ordinal)
                    ? $"货项{identity["Style:".Length..]}"
                    : $"第{identity["Row:".Length..]}项";
            return $"{rowLabel}{label}";
        }

        private static string NormalizeText(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
