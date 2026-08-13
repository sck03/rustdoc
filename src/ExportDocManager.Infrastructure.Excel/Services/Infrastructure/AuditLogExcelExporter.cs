using ClosedXML.Excel;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure;

public sealed class AuditLogExcelExporter : IAuditLogExcelExporter
{
    private static readonly IReadOnlyList<(string Header, double Width)> ExportColumns =
    [
        ("时间", 20),
        ("实体", 16),
        ("动作", 12),
        ("实体ID", 22),
        ("操作人", 16),
        ("变更前", 50),
        ("变更后", 50)
    ];

    public async Task ExportAsync(
        IReadOnlyList<AuditLog> rows,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        await AtomicFileHelper.WriteFileAtomicAsync(
            destinationPath,
            (tempFilePath, token) => Task.Run(
                () =>
                {
                    using var output = File.Create(tempFilePath);
                    WriteWorkbook(output, rows, token);
                },
                token),
            cancellationToken);
    }

    public async Task<byte[]> ExportBytesAsync(
        IReadOnlyList<AuditLog> rows,
        CancellationToken cancellationToken = default)
    {
        await using var output = new MemoryStream();
        await Task.Run(() => WriteWorkbook(output, rows, cancellationToken), cancellationToken);
        return output.ToArray();
    }

    private static void WriteWorkbook(
        Stream output,
        IReadOnlyList<AuditLog> rows,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("AuditLogs");

        for (int index = 0; index < ExportColumns.Count; index++)
        {
            var column = ExportColumns[index];
            int columnNumber = index + 1;
            worksheet.Cell(1, columnNumber).Value = column.Header;
            worksheet.Cell(1, columnNumber).Style.Font.Bold = true;
            worksheet.Column(columnNumber).Width = column.Width;
        }

        int rowNumber = 2;
        foreach (var row in rows ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            worksheet.Cell(rowNumber, 1).Value = row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            worksheet.Cell(rowNumber, 2).Value = row.EntityName ?? string.Empty;
            worksheet.Cell(rowNumber, 3).Value = row.Action ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = row.EntityId ?? string.Empty;
            worksheet.Cell(rowNumber, 5).Value = row.UserId ?? string.Empty;
            worksheet.Cell(rowNumber, 6).Value = row.OldValues ?? string.Empty;
            worksheet.Cell(rowNumber, 7).Value = row.NewValues ?? string.Empty;
            rowNumber++;
        }

        worksheet.Range(1, 1, 1, ExportColumns.Count).SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(output);
    }
}
