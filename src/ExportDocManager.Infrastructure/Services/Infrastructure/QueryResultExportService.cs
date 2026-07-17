using ClosedXML.Excel;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class QueryResultExportService : IQueryResultExportService
    {
        private const int ExportPageSize = 500;

        private static readonly IReadOnlyList<(string Header, double Width)> ExportColumns =
        [
            ("发票号", 18),
            ("日期", 12),
            ("合同号", 18),
            ("客户", 28),
            ("出口商", 28),
            ("目的国", 14),
            ("贸易条款", 12),
            ("船期/航期", 12),
            ("运输方式", 14),
            ("总箱数", 10),
            ("总数量", 12),
            ("总金额", 14),
            ("币种", 8),
            ("类型", 12)
        ];

        private readonly IQueryReadRepository _queryReadRepository;

        public QueryResultExportService(IQueryReadRepository queryReadRepository)
        {
            _queryReadRepository = queryReadRepository ?? throw new ArgumentNullException(nameof(queryReadRepository));
        }

        public async Task<QueryResultExportResult> ExportToExcelAsync(
            QueryPageQuery query,
            string filePath,
            IProgress<OperationProgressUpdate> progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string destinationPath = Path.GetFullPath(filePath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            progress?.Report(new OperationProgressUpdate
            {
                StatusText = "正在读取查询结果",
                DetailText = "系统正在准备导出数据。"
            });

            var firstPage = await _queryReadRepository.QueryPageAsync(
                BuildExportPageQuery(query, 1),
                cancellationToken);
            if (firstPage.TotalCount <= 0 || firstPage.Items.Count == 0)
            {
                return new QueryResultExportResult(0, destinationPath);
            }

            int exportedCount = 0;
            await AtomicFileHelper.WriteFileAtomicAsync(
                destinationPath,
                async (tempFilePath, ct) =>
                {
                    exportedCount = await Task.Run(
                        () => WriteWorkbook(tempFilePath, query, firstPage, progress, ct),
                        ct);
                },
                cancellationToken);

            progress?.Report(new OperationProgressUpdate
            {
                StatusText = "导出完成",
                DetailText = $"已写入 {exportedCount} 行。",
                ProgressPercent = 100
            });

            return new QueryResultExportResult(exportedCount, destinationPath);
        }

        public async Task<QueryResultExportBytesResult> ExportToExcelBytesAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var firstPage = await _queryReadRepository.QueryPageAsync(
                BuildExportPageQuery(query, 1),
                cancellationToken);

            using var workbook = new XLWorkbook();
            int exportedCount = await PopulateWorkbookAsync(
                workbook,
                query,
                firstPage,
                progress: null,
                cancellationToken);
            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return new QueryResultExportBytesResult(output.ToArray(), exportedCount);
        }

        private async Task<int> WriteWorkbook(
            string tempFilePath,
            QueryPageQuery query,
            PagedResult<Invoice> firstPage,
            IProgress<OperationProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var workbook = new XLWorkbook();
            int writtenCount = await PopulateWorkbookAsync(
                workbook,
                query,
                firstPage,
                progress,
                cancellationToken);
            workbook.SaveAs(tempFilePath);
            return writtenCount;
        }

        private async Task<int> PopulateWorkbookAsync(
            IXLWorkbook workbook,
            QueryPageQuery query,
            PagedResult<Invoice> firstPage,
            IProgress<OperationProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var worksheet = workbook.Worksheets.Add("查询结果");
            WriteHeader(worksheet);

            int writtenCount = WriteRows(
                worksheet,
                firstPage.Items,
                startRowNumber: 2,
                firstPage.TotalCount,
                progress,
                cancellationToken);

            for (int pageNumber = firstPage.PageNumber + 1;
                 pageNumber <= Math.Max(firstPage.TotalPages, 1) && writtenCount < firstPage.TotalCount;
                 pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextPage = await _queryReadRepository.QueryPageAsync(
                    BuildExportPageQuery(query, pageNumber),
                    cancellationToken);
                if (nextPage.Items.Count == 0)
                {
                    break;
                }

                writtenCount = WriteRows(
                    worksheet,
                    nextPage.Items,
                    writtenCount + 2,
                    firstPage.TotalCount,
                    progress,
                    cancellationToken);
            }

            return writtenCount;
        }

        private static QueryPageQuery BuildExportPageQuery(QueryPageQuery query, int pageNumber)
        {
            return (query ?? new QueryPageQuery()) with
            {
                PageNumber = Math.Max(1, pageNumber),
                PageSize = ExportPageSize
            };
        }

        private static void WriteHeader(IXLWorksheet worksheet)
        {
            for (int index = 0; index < ExportColumns.Count; index++)
            {
                var column = ExportColumns[index];
                int columnNumber = index + 1;
                worksheet.Cell(1, columnNumber).Value = column.Header;
                worksheet.Cell(1, columnNumber).Style.Font.Bold = true;
                worksheet.Column(columnNumber).Width = Math.Max(1d, column.Width);
            }

            worksheet.Range(1, 1, 1, ExportColumns.Count).SetAutoFilter();
            worksheet.SheetView.FreezeRows(1);
        }

        private static int WriteRows(
            IXLWorksheet worksheet,
            IReadOnlyList<Invoice> invoices,
            int startRowNumber,
            int totalCount,
            IProgress<OperationProgressUpdate> progress,
            CancellationToken cancellationToken)
        {
            int rowNumber = Math.Max(2, startRowNumber);
            foreach (var invoice in invoices ?? Array.Empty<Invoice>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteRow(worksheet, rowNumber, QueryResultRowMapper.FromInvoice(invoice));

                int writtenCount = rowNumber - 1;
                ReportExportProgress(progress, Math.Min(writtenCount, totalCount), totalCount);
                rowNumber++;
            }

            return rowNumber - 2;
        }

        private static void WriteRow(IXLWorksheet worksheet, int rowNumber, QueryResultRow row)
        {
            worksheet.Cell(rowNumber, 1).Value = row.InvoiceNo;
            worksheet.Cell(rowNumber, 2).Value = row.InvoiceDate;
            worksheet.Cell(rowNumber, 3).Value = row.ContractNo;
            worksheet.Cell(rowNumber, 4).Value = row.CustomerName;
            worksheet.Cell(rowNumber, 5).Value = row.ExporterName;
            worksheet.Cell(rowNumber, 6).Value = row.DestinationCountry;
            worksheet.Cell(rowNumber, 7).Value = row.TradeTerms;
            worksheet.Cell(rowNumber, 8).Value = row.ShipmentDate;
            worksheet.Cell(rowNumber, 9).Value = row.TransportMode;
            worksheet.Cell(rowNumber, 10).Value = row.TotalCartons;
            worksheet.Cell(rowNumber, 11).Value = row.TotalQuantity;
            worksheet.Cell(rowNumber, 12).Value = row.TotalAmount;
            worksheet.Cell(rowNumber, 13).Value = row.Currency;
            worksheet.Cell(rowNumber, 14).Value = row.Type;
        }

        private static void ReportExportProgress(
            IProgress<OperationProgressUpdate> progress,
            int writtenCount,
            int totalCount)
        {
            if (progress == null || totalCount <= 0)
            {
                return;
            }

            if (writtenCount < totalCount && writtenCount % 20 != 0)
            {
                return;
            }

            progress.Report(new OperationProgressUpdate
            {
                StatusText = "正在生成导出文件",
                DetailText = $"已写入 {writtenCount} / {totalCount} 行。",
                ProgressPercent = (int)Math.Round(writtenCount * 100d / totalCount)
            });
        }
    }
}
