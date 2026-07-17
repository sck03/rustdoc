using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.SingleWindow
{
    public static partial class SingleWindowDraftStateHelper
    {
        private static Dictionary<string, string> BuildCustomsCooBaselineMap(CustomsCooDocument document)
        {
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (document == null)
            {
                return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            }

            foreach (var property in GetStringProperties(typeof(CustomsCooDocument), CustomsCooDocumentEditableExclusions))
            {
                map[property.Name] = NormalizeText(property.GetValue(document) as string);
            }

            foreach (var row in document.Items ?? [])
            {
                string identity = BuildGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo);
                foreach (var property in GetStringProperties(typeof(CustomsCooItem), CustomsCooItemEditableExclusions))
                {
                    map[BuildGoodsFieldKey(identity, property.Name)] = NormalizeText(property.GetValue(row) as string);
                }
            }

            return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> BuildCustomsCooSourceDiffBaselineMap(CustomsCooDocument document)
        {
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (document == null)
            {
                return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            }

            foreach (var property in GetStringProperties(typeof(CustomsCooDocument), CustomsCooDocumentEditableExclusions))
            {
                if (!CustomsCooSourceDiffFields.Contains(property.Name))
                {
                    continue;
                }

                map[property.Name] = NormalizeText(property.GetValue(document) as string);
            }

            foreach (var row in document.Items ?? [])
            {
                string identity = BuildGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo);
                foreach (var property in GetStringProperties(typeof(CustomsCooItem), CustomsCooItemEditableExclusions))
                {
                    if (!CustomsCooSourceDiffGoodsFields.Contains(property.Name))
                    {
                        continue;
                    }

                    map[BuildGoodsFieldKey(identity, property.Name)] = NormalizeText(property.GetValue(row) as string);
                }
            }

            return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        private static Dictionary<string, string> BuildAgentConsignmentBaselineMap(AgentConsignmentDocument document)
        {
            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (document == null)
            {
                return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            }

            foreach (var property in GetStringProperties(typeof(AgentConsignmentDocument), AgentConsignmentEditableExclusions))
            {
                map[property.Name] = NormalizeText(property.GetValue(document) as string);
            }

            return map.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }

        private static HashSet<string> InferCustomsCooLockedFieldsFromBaseline(CustomsCooDocument stored)
        {
            var inferred = new HashSet<string>(StringComparer.Ordinal);
            if (stored == null)
            {
                return inferred;
            }

            var baselineMap = DeserializeBaselineMap(stored.SourceBaselineJson);
            if (baselineMap.Count == 0)
            {
                return inferred;
            }

            var currentMap = BuildCustomsCooBaselineMap(stored);
            foreach (var entry in currentMap)
            {
                string currentValue = NormalizeText(entry.Value);
                string baselineValue = NormalizeText(
                    baselineMap.TryGetValue(entry.Key, out var rawBaselineValue)
                        ? rawBaselineValue
                        : string.Empty);
                if (!string.Equals(currentValue, baselineValue, StringComparison.Ordinal))
                {
                    inferred.Add(entry.Key);
                }
            }

            return inferred;
        }

        private static string SerializeBaselineMap(IReadOnlyDictionary<string, string> baselineMap)
        {
            if (baselineMap == null || baselineMap.Count == 0)
            {
                return string.Empty;
            }

            return JsonSerializer.Serialize(
                baselineMap.OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                JsonOptions);
        }

        private static Dictionary<string, string> DeserializeBaselineMap(string baselineJson)
        {
            if (string.IsNullOrWhiteSpace(baselineJson))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(baselineJson, JsonOptions)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static (int Count, string Summary) BuildSourceDiffSummary(
            IReadOnlyDictionary<string, string> baselineMap,
            IReadOnlyDictionary<string, string> currentMap)
        {
            var keys = baselineMap.Keys
                .Concat(currentMap.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToList();

            var lines = new List<(string Key, string Text, bool IsIntroducedValue)>();
            foreach (var key in keys)
            {
                string before = NormalizeText(baselineMap.TryGetValue(key, out var previous) ? previous : string.Empty);
                string after = NormalizeText(currentMap.TryGetValue(key, out var current) ? current : string.Empty);
                if (string.Equals(before, after, StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add((
                    key,
                    $"{FormatFieldKey(key)}: {FormatDiffValue(before)} -> {FormatDiffValue(after)}",
                    string.IsNullOrWhiteSpace(before) && !string.IsNullOrWhiteSpace(after)));
            }

            if (lines.Count == 0)
            {
                return (0, string.Empty);
            }

            var orderedLines = lines
                .OrderBy(item => item.IsIntroducedValue)
                .ThenBy(item => GetSourceDiffPriority(item.Key))
                .ThenBy(item => FormatFieldKey(item.Key), StringComparer.Ordinal)
                .Select(item => item.Text)
                .ToList();

            string summary = string.Join(
                Environment.NewLine,
                orderedLines.Take(SourceDiffSummaryLineLimit));

            if (orderedLines.Count > SourceDiffSummaryLineLimit)
            {
                summary += $"{Environment.NewLine}还有 {orderedLines.Count - SourceDiffSummaryLineLimit} 项源资料变化未展开。";
            }

            return (orderedLines.Count, summary);
        }

        private static int GetSourceDiffPriority(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return 10;
            }

            if (TryParseGoodsFieldKey(key, out _, out var propertyName))
            {
                return SourceDiffPriorityGoodsFields.Contains(propertyName) ? 1 : 5;
            }

            return SourceDiffPriorityFields.Contains(key) ? 0 : 10;
        }

        private static string FormatDiffValue(string value) =>
            string.IsNullOrWhiteSpace(value) ? "空白" : value;
    }
}
