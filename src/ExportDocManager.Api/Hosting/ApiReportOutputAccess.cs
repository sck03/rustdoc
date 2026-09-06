using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    internal static async Task DemandReportOutputAccessAsync(
        IServiceProvider services,
        ReportDocumentType reportType,
        IReadOnlyCollection<int> sourceIds,
        string action,
        CancellationToken cancellationToken)
    {
        var scope = services.GetRequiredService<BusinessDataAccessScope>();
        if (!scope.UsesPostgreSql) return;
        string resource = reportType == ReportDocumentType.ExportDocument
            ? PermissionResourceCatalog.InvoiceOutput
            : PermissionResourceCatalog.PaymentOutput;
        scope.DemandPermission(resource, action);
        if (action == PermissionAction.SendEmail)
        {
            scope.DemandPermission(PermissionResourceCatalog.EmailDelivery, PermissionAction.Send);
        }

        int[] ids = sourceIds.Distinct().ToArray();
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        IBusinessOwnedEntity[] sources = reportType switch
        {
            ReportDocumentType.ExportDocument => await scope.ApplyInvoiceScope(context.Invoices.AsNoTracking())
                .Where(item => ids.Contains(item.Id))
                .Select(item => new Invoice
                {
                    OwnerUserId = item.OwnerUserId,
                    DepartmentId = item.DepartmentId,
                    CompanyScope = item.CompanyScope
                }).ToArrayAsync(cancellationToken),
            ReportDocumentType.PaymentVoucher => await scope.ApplyPaymentScope(context.Payments.AsNoTracking())
                .Where(item => ids.Contains(item.Id))
                .Select(item => new Payment
                {
                    OwnerUserId = item.OwnerUserId,
                    DepartmentId = item.DepartmentId,
                    CompanyScope = item.CompanyScope
                }).ToArrayAsync(cancellationToken),
            _ => throw new ServiceValidationException("报表类型无效。")
        };
        if (sources.Length != ids.Length) throw new ResourceNotFoundException("单据不存在或不在当前账号的查看范围内。");
        foreach (var source in sources) scope.DemandRecordAccess(source, resource, action);
    }
}
