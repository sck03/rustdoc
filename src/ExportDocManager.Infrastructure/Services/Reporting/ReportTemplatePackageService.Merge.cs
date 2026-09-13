using ExportDocManager.Models;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private static List<ReportTemplateConfig> MergeTemplateRows(
            List<ReportTemplateConfig> existing, List<ReportTemplateConfig> incoming, ReportTemplateImportStrategy strategy) =>
            MergeRows(existing, incoming, strategy, BuildTemplateRowKey, CloneRow, (current, next) =>
            {
                current.Name = next.Name;
                current.WithSeal = next.WithSeal;
                return current;
            });

        private static List<BatchExportItem> MergeBatchExportItems(
            List<BatchExportItem> existing, List<BatchExportItem> incoming, ReportTemplateImportStrategy strategy) =>
            MergeRows(existing, incoming, strategy, BuildTemplateItemKey, CloneItem);

        private static List<PaymentTemplateItem> MergePaymentTemplateItems(
            List<PaymentTemplateItem> existing, List<PaymentTemplateItem> incoming, ReportTemplateImportStrategy strategy) =>
            MergeRows(existing, incoming, strategy, BuildTemplateItemKey, ClonePaymentItem);

        private static List<T> MergeRows<T>(IEnumerable<T>? existing, IEnumerable<T> incoming,
            ReportTemplateImportStrategy strategy, Func<T, string> key, Func<T, T> clone, Func<T, T, T>? merge = null)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite) return incoming.Select(clone).ToList();
            var result = existing?.Select(clone).ToList() ?? [];
            var positions = result.Select((item, index) => (Key: key(item), Index: index))
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.OrdinalIgnoreCase);
            foreach (var item in incoming)
            {
                string itemKey = key(item);
                if (!positions.TryGetValue(itemKey, out int index))
                {
                    positions.Add(itemKey, result.Count);
                    result.Add(clone(item));
                }
                else if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    result[index] = merge == null ? clone(item) : merge(result[index], item);
                }
            }
            return result;
        }

        private static string BuildTemplateRowKey(ReportTemplateConfig row) =>
            $"{row?.Type}|{row?.FileName}";

        private static string BuildTemplateItemKey(TemplateItemBase item) =>
            $"{item?.ReportType}|{item?.TemplatePath}|{item?.Name}";

        private static ReportTemplateConfig CloneRow(ReportTemplateConfig row)
        {
            bool supportsSeal = ReportTemplateCatalogLoader.ResolveCatalogReportType(row?.Type, row?.FileName) !=
                ReportDocumentType.PaymentVoucher;
            return new ReportTemplateConfig
            {
                Type = row?.Type ?? string.Empty,
                Name = row?.Name ?? string.Empty,
                FileName = row?.FileName ?? string.Empty,
                WithSeal = supportsSeal ? row?.WithSeal ?? true : null
            };
        }

        private static BatchExportItem CloneItem(BatchExportItem item)
        {
            return new BatchExportItem
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = item?.ReportType ?? string.Empty,
                IsEnabled = item?.IsEnabled ?? true,
                ShowSeal = item?.ShowSeal ?? true
            };
        }

        private static PaymentTemplateItem ClonePaymentItem(PaymentTemplateItem item)
        {
            return new PaymentTemplateItem
            {
                Name = item?.Name ?? string.Empty,
                TemplatePath = item?.TemplatePath ?? string.Empty,
                ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                IsEnabled = item?.IsEnabled ?? true
            };
        }

        private List<BatchExportItemManifest> BuildExportManifestItems(IEnumerable<BatchExportItem>? items)
        {
            return (items ?? Enumerable.Empty<BatchExportItem>())
                .Select(item => _referencePolicy.TryNormalize(
                    item?.TemplatePath,
                    ReportDocumentType.ExportDocument,
                    out string templatePath)
                    ? new BatchExportItemManifest
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.ExportDocument.ToString(),
                        IsEnabled = item?.IsEnabled ?? true,
                        ShowSeal = item?.ShowSeal ?? true
                    }
                    : null)
                .OfType<BatchExportItemManifest>()
                .ToList();
        }

        private List<PaymentTemplateItemManifest> BuildPaymentManifestItems(IEnumerable<PaymentTemplateItem>? items)
        {
            return (items ?? Enumerable.Empty<PaymentTemplateItem>())
                .Select(item => _referencePolicy.TryNormalize(
                    item?.TemplatePath,
                    ReportDocumentType.PaymentVoucher,
                    out string templatePath)
                    ? new PaymentTemplateItemManifest
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                        IsEnabled = item?.IsEnabled ?? true
                    }
                    : null)
                .OfType<PaymentTemplateItemManifest>()
                .ToList();
        }

        private List<BatchExportItem> BuildImportedExportItems(IEnumerable<BatchExportItemManifest> items)
        {
            return (items ?? Enumerable.Empty<BatchExportItemManifest>())
                .Select(item => _referencePolicy.TryNormalize(
                    item?.TemplatePath,
                    ReportDocumentType.ExportDocument,
                    out string templatePath)
                    ? new BatchExportItem
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.ExportDocument.ToString(),
                        IsEnabled = item?.IsEnabled ?? true,
                        ShowSeal = item?.ShowSeal ?? true
                    }
                    : null)
                .OfType<BatchExportItem>()
                .ToList();
        }

        private List<PaymentTemplateItem> BuildImportedPaymentItems(IEnumerable<PaymentTemplateItemManifest> items)
        {
            return (items ?? Enumerable.Empty<PaymentTemplateItemManifest>())
                .Select(item => _referencePolicy.TryNormalize(
                    item?.TemplatePath,
                    ReportDocumentType.PaymentVoucher,
                    out string templatePath)
                    ? new PaymentTemplateItem
                    {
                        Name = item?.Name ?? string.Empty,
                        TemplatePath = templatePath,
                        ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                        IsEnabled = item?.IsEnabled ?? true
                    }
                    : null)
                .OfType<PaymentTemplateItem>()
                .ToList();
        }

    }
}
