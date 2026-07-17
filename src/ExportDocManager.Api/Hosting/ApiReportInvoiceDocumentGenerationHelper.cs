using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        internal static async Task<ApiInvoiceGeneratedDocumentSet> GenerateInvoiceDocumentPdfFilesAsync(
            IServiceProvider provider,
            ApiBackgroundJobExecutionContext jobContext,
            int invoiceId,
            IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> items,
            string tempRoot,
            bool includeMergedPdf,
            int startProgress,
            int endProgress,
            string progressOutputPath)
        {
            Directory.CreateDirectory(tempRoot);

            var invoiceService = provider.GetRequiredService<IInvoiceService>();
            var reportPdfRenderService = provider.GetRequiredService<IReportPdfRenderService>();
            var pdfMergeService = provider.GetRequiredService<IPdfMergeService>();
            var settingsService = provider.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync().ConfigureAwait(false);
            string fileNamePattern = settingsService.Settings?.BatchExport?.OutputFileNamePattern
                ?? BatchExportPathHelper.DefaultFileNamePattern;

            var invoice = await invoiceService.GetInvoiceByIdAsync(invoiceId).ConfigureAwait(false);
            if (invoice == null)
            {
                throw new KeyNotFoundException("未找到指定的发票。");
            }

            string invoiceNo = invoice.InvoiceNo ?? $"Invoice_{invoiceId}";
            string customerName = invoice.CustomerNameEN ?? string.Empty;
            var exportDate = DateTime.Now;
            var generatedFiles = new List<string>();
            var entries = new List<ApiInvoiceGeneratedDocumentEntry>();
            int renderProgressRange = Math.Max(1, endProgress - startProgress - 12);

            for (var index = 0; index < items.Count; index++)
            {
                jobContext.CancellationToken.ThrowIfCancellationRequested();
                var item = items[index];
                int progress = startProgress + (int)Math.Round(index * (double)renderProgressRange / items.Count);
                jobContext.Report(
                    progress,
                    "正在生成单据 PDF",
                    item.Name,
                    progressOutputPath);

                string pdfFileName = BatchExportPathHelper.BuildDocumentFileName(
                    tempRoot,
                    fileNamePattern,
                    invoiceNo,
                    customerName,
                    item.Name,
                    exportDate);
                string pdfPath = Path.Combine(tempRoot, pdfFileName);

                var pdfResult = await reportPdfRenderService.RenderInvoicePdfAsync(
                        new ReportPdfRenderRequest
                        {
                            SourceId = invoiceId,
                            ReportType = ReportDocumentType.ExportDocument,
                            TemplatePath = item.TemplatePath,
                            WithSeal = item.WithSeal,
                            DestinationPath = pdfPath,
                            DocumentTitle = $"{invoiceNo}-{item.Name}"
                        },
                        jobContext.CancellationToken)
                    .ConfigureAwait(false);

                generatedFiles.Add(pdfResult.DestinationPath);
                entries.Add(new ApiInvoiceGeneratedDocumentEntry(pdfResult.DestinationPath, pdfFileName));
            }

            if (includeMergedPdf && generatedFiles.Count > 1)
            {
                jobContext.Report(
                    endProgress - 10,
                    "正在合并单据 PDF",
                    invoiceNo,
                    progressOutputPath);

                string mergedFileName = BatchExportPathHelper.BuildMergedPdfFileName(
                    tempRoot,
                    invoiceNo,
                    customerName,
                    exportDate);
                string mergedPath = Path.Combine(tempRoot, mergedFileName);
                pdfMergeService.Merge(generatedFiles, mergedPath, jobContext.CancellationToken);
                entries.Add(new ApiInvoiceGeneratedDocumentEntry(mergedPath, mergedFileName));
            }

            return new ApiInvoiceGeneratedDocumentSet(
                invoiceNo,
                customerName,
                invoice.CustomerId,
                exportDate,
                generatedFiles,
                entries);
        }

        internal sealed class ApiInvoiceGeneratedDocumentSet
        {
            public ApiInvoiceGeneratedDocumentSet(
                string invoiceNo,
                string customerName,
                int customerId,
                DateTime exportDate,
                IReadOnlyList<string> generatedFiles,
                IReadOnlyList<ApiInvoiceGeneratedDocumentEntry> entries)
            {
                InvoiceNo = invoiceNo ?? string.Empty;
                CustomerName = customerName ?? string.Empty;
                CustomerId = customerId;
                ExportDate = exportDate;
                GeneratedFiles = generatedFiles ?? Array.Empty<string>();
                Entries = entries ?? Array.Empty<ApiInvoiceGeneratedDocumentEntry>();
            }

            public string InvoiceNo { get; }

            public string CustomerName { get; }

            public int CustomerId { get; }

            public DateTime ExportDate { get; }

            public IReadOnlyList<string> GeneratedFiles { get; }

            public IReadOnlyList<ApiInvoiceGeneratedDocumentEntry> Entries { get; }
        }

        internal sealed record ApiInvoiceGeneratedDocumentEntry(
            string SourcePath,
            string EntryName);
    }
}
