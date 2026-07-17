using System.Reflection;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowExportReviewRepairHelper
    {
        private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomsCooDocumentPropertyMap =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(CustomsCooDocument));

        private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomsCooItemPropertyMap =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(CustomsCooItem));

        private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomsCooAttachmentPropertyMap =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(CustomsCooAttachment));

        private static readonly IReadOnlyDictionary<string, PropertyInfo> AgentConsignmentDocumentPropertyMap =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(AgentConsignmentDocument));

        public static int RepairCustomsCooGroups(
            CustomsCooDocument document,
            CustomsCooDocument defaults,
            IEnumerable<string> groupKeys)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(defaults);

            int repairedGroupCount = 0;
            foreach (string groupKey in NormalizeGroupKeys(groupKeys))
            {
                int groupChangeCount = 0;
                if (string.Equals(groupKey, SingleWindowExportReviewRepairCatalog.CustomsCooGoodsGroupKey, StringComparison.Ordinal))
                {
                    groupChangeCount = ApplyCustomsCooGoodsDefaults(document, defaults);
                }
                else if (string.Equals(groupKey, SingleWindowExportReviewRepairCatalog.CustomsCooAttachmentGroupKey, StringComparison.Ordinal))
                {
                    groupChangeCount = ClearCustomsCooAttachmentValues(document);
                }
                else if (SingleWindowExportReviewRepairCatalog.CustomsCooScopedClearOptionsByGroup.TryGetValue(groupKey, out var options))
                {
                    groupChangeCount = ApplyScopedDefaults(
                        options,
                        SingleWindowExportReviewRepairCatalog.CustomsCooScopedHeaderFieldKeysByCategory,
                        fieldKeys => ApplyStringDefaults(
                            document,
                            CustomsCooDocumentPropertyMap,
                            defaults,
                            CustomsCooDocumentPropertyMap,
                            fieldKeys,
                            NormalizeCustomsCooHeaderValue));
                }

                if (groupChangeCount > 0)
                {
                    repairedGroupCount++;
                }
            }

            return repairedGroupCount;
        }

        public static int RepairAgentConsignmentGroups(
            AgentConsignmentDocument document,
            AgentConsignmentDocument defaults,
            IEnumerable<string> groupKeys)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(defaults);

            int repairedGroupCount = 0;
            foreach (string groupKey in NormalizeGroupKeys(groupKeys))
            {
                if (!SingleWindowExportReviewRepairCatalog.AgentConsignmentScopedClearOptionsByGroup.TryGetValue(groupKey, out var options))
                {
                    continue;
                }

                int groupChangeCount = ApplyScopedDefaults(
                    options,
                    SingleWindowExportReviewRepairCatalog.AgentConsignmentScopedFieldKeysByCategory,
                    fieldKeys => ApplyStringDefaults(
                        document,
                        AgentConsignmentDocumentPropertyMap,
                        defaults,
                        AgentConsignmentDocumentPropertyMap,
                        fieldKeys,
                        NormalizeAgentConsignmentValue));
                if (groupChangeCount > 0)
                {
                    repairedGroupCount++;
                }
            }

            return repairedGroupCount;
        }

        private static IEnumerable<string> NormalizeGroupKeys(IEnumerable<string> groupKeys)
        {
            return (groupKeys ?? Array.Empty<string>())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.Ordinal);
        }

        private static int ApplyScopedDefaults(
            IEnumerable<GroupScopedClearOption> options,
            IReadOnlyDictionary<string, IReadOnlyList<string>> fieldKeysByCategory,
            Func<IEnumerable<string>, int> applyDefaults)
        {
            ArgumentNullException.ThrowIfNull(fieldKeysByCategory);
            ArgumentNullException.ThrowIfNull(applyDefaults);

            int changeCount = 0;
            foreach (var option in options ?? Array.Empty<GroupScopedClearOption>())
            {
                if (!fieldKeysByCategory.TryGetValue(option.Key, out var fieldKeys))
                {
                    continue;
                }

                changeCount += applyDefaults(fieldKeys);
            }

            return changeCount;
        }

        private static int ApplyStringDefaults(
            object target,
            IReadOnlyDictionary<string, PropertyInfo> targetPropertyMap,
            object defaults,
            IReadOnlyDictionary<string, PropertyInfo> defaultPropertyMap,
            IEnumerable<string> fieldKeys,
            Func<string, string, string> normalize)
        {
            int changeCount = 0;
            foreach (string fieldKey in (fieldKeys ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal))
            {
                if (!defaultPropertyMap.TryGetValue(fieldKey, out var defaultProperty))
                {
                    continue;
                }

                string suggestedValue = normalize(fieldKey, defaultProperty.GetValue(defaults) as string);
                changeCount += SingleWindowEditorFieldHelper.ApplyStringPropertyChange(
                    target,
                    targetPropertyMap,
                    fieldKey,
                    suggestedValue);
            }

            return changeCount;
        }

        private static string NormalizeCustomsCooHeaderValue(string fieldKey, string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(normalized) &&
                   SingleWindowExportReviewRepairCatalog.CustomsCooHeaderDefaultFallbacks.TryGetValue(fieldKey, out string fallback)
                ? fallback
                : normalized;
        }

        private static string NormalizeAgentConsignmentValue(string fieldKey, string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return string.Equals(fieldKey, nameof(AgentConsignmentDocument.OperType), StringComparison.Ordinal) &&
                   string.IsNullOrWhiteSpace(normalized)
                ? "1"
                : normalized;
        }

        private static int ApplyCustomsCooGoodsDefaults(CustomsCooDocument document, CustomsCooDocument defaults)
        {
            var currentItems = (document.Items ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.GNo)
                .ToList();
            var defaultItems = (defaults.Items ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.GNo)
                .ToList();
            var fieldKeys = SingleWindowExportReviewRepairCatalog.CustomsCooScopedGoodsFieldKeysByCategory.Values
                .SelectMany(item => item)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            int changeCount = 0;
            for (int index = 0; index < currentItems.Count && index < defaultItems.Count; index++)
            {
                changeCount += ApplyStringDefaults(
                    currentItems[index],
                    CustomsCooItemPropertyMap,
                    defaultItems[index],
                    CustomsCooItemPropertyMap,
                    fieldKeys,
                    (_, value) => value?.Trim() ?? string.Empty);
            }

            return changeCount;
        }

        private static int ClearCustomsCooAttachmentValues(CustomsCooDocument document)
        {
            int changeCount = 0;
            var fieldKeys = SingleWindowExportReviewRepairCatalog.CustomsCooScopedAttachmentStringFieldKeysByCategory.Values
                .SelectMany(item => item)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var attachment in document.Attachments ?? [])
            {
                if (attachment == null)
                {
                    continue;
                }

                changeCount += SingleWindowEditorFieldHelper.ClearStringPropertyValues(
                    attachment,
                    CustomsCooAttachmentPropertyMap,
                    fieldKeys);

                if (attachment.IsDelay)
                {
                    attachment.IsDelay = false;
                    changeCount++;
                }
            }

            return changeCount;
        }
    }
}
