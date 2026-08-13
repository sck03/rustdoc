using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Infrastructure;

public sealed class UnsupportedAuditLogExcelExporter : IAuditLogExcelExporter
{
    private static InfrastructureServiceException CreateException() =>
        new("当前产品未安装 Excel 能力模块，无法导出审计日志工作簿。");

    public Task ExportAsync(
        IReadOnlyList<AuditLog> rows,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    public Task<byte[]> ExportBytesAsync(
        IReadOnlyList<AuditLog> rows,
        CancellationToken cancellationToken = default) =>
        Task.FromException<byte[]>(CreateException());
}
