using ExportDocManager.Models;

namespace ExportDocManager.Services.Reporting
{
    public sealed partial class ReportTemplatePackageService
    {
        private static List<ReportTemplateConfig> MergeTemplateRows(
            List<ReportTemplateConfig> existing,
            List<ReportTemplateConfig> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(CloneRow).ToList();
            }

            var result = existing?.Select(CloneRow).ToList() ?? new List<ReportTemplateConfig>();
            var map = result.ToDictionary(BuildTemplateRowKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var row in incoming)
            {
                string key = BuildTemplateRowKey(row);
                if (!map.ContainsKey(key))
                {
                    var added = CloneRow(row);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = row.Name;
                    map[key].WithSeal = row.WithSeal;
                }
            }

            return result;
        }

        private static List<BatchExportItem> MergeBatchExportItems(
            List<BatchExportItem> existing,
            List<BatchExportItem> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(CloneItem).ToList();
            }

            var result = existing?.Select(CloneItem).ToList() ?? new List<BatchExportItem>();
            var map = result.ToDictionary(BuildBatchItemKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var item in incoming)
            {
                string key = BuildBatchItemKey(item);
                if (!map.ContainsKey(key))
                {
                    var added = CloneItem(item);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = item.Name;
                    map[key].TemplatePath = item.TemplatePath;
                    map[key].ReportType = item.ReportType;
                    map[key].IsEnabled = item.IsEnabled;
                    map[key].ShowSeal = item.ShowSeal;
                }
            }

            return result;
        }

        private static List<PaymentTemplateItem> MergePaymentTemplateItems(
            List<PaymentTemplateItem> existing,
            List<PaymentTemplateItem> incoming,
            ReportTemplateImportStrategy strategy)
        {
            if (strategy == ReportTemplateImportStrategy.Overwrite)
            {
                return incoming.Select(ClonePaymentItem).ToList();
            }

            var result = existing?.Select(ClonePaymentItem).ToList() ?? new List<PaymentTemplateItem>();
            var map = result.ToDictionary(BuildTemplateItemKey, item => item, StringComparer.OrdinalIgnoreCase);

            foreach (var item in incoming)
            {
                string key = BuildTemplateItemKey(item);
                if (!map.ContainsKey(key))
                {
                    var added = ClonePaymentItem(item);
                    result.Add(added);
                    map[key] = added;
                    continue;
                }

                if (strategy == ReportTemplateImportStrategy.Merge)
                {
                    map[key].Name = item.Name;
                    map[key].TemplatePath = item.TemplatePath;
                    map[key].ReportType = ReportDocumentType.PaymentVoucher.ToString();
                    map[key].IsEnabled = item.IsEnabled;
                }
            }

            return result;
        }

        private static string BuildTemplateRowKey(ReportTemplateConfig row) =>
            $"{row?.Type}|{row?.FileName}";

        private static string BuildBatchItemKey(BatchExportItem item) =>
            BuildTemplateItemKey(item);

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
