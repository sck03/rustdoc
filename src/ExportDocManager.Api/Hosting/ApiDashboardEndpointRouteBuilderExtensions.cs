using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Dashboard;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/dashboard", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IDashboardService dashboardService,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                var dashboard = await dashboardService.GetDashboardAsync(cancellationToken);
                return Results.Ok(ToDashboardResponse(dashboard));
            })
            .WithName("GetDashboard");
        }

        private static ApiDashboardResponse ToDashboardResponse(DashboardSnapshot dashboard)
        {
            return new ApiDashboardResponse
            {
                MonthlyExportAmount = dashboard.MonthlyExportAmount,
                MonthlyProfit = dashboard.MonthlyProfit,
                MonthlyTaxRefund = dashboard.MonthlyTaxRefund,
                PendingCount = dashboard.PendingCount,
                ShippedCount = dashboard.ShippedCount,
                TotalActiveCount = dashboard.TotalActiveCount,
                SingleWindowStatusSummary = dashboard.SingleWindowStatusSummary,
                RecentInvoices = dashboard.RecentInvoices
                    .Select(invoice => new ApiDashboardRecentInvoiceDto
                    {
                        Id = invoice.Id,
                        InvoiceNo = invoice.InvoiceNo,
                        Status = invoice.Status,
                        StatusText = GetInvoiceStatusText(invoice.Status),
                        Type = invoice.Type,
                        InvoiceDate = invoice.InvoiceDate,
                        TotalAmount = invoice.TotalAmount,
                        CustomerNameEN = invoice.CustomerNameEN
                    })
                    .ToArray(),
                TodoItems = dashboard.TodoItems
                    .Select(item => new ApiDashboardTodoItemDto
                    {
                        Title = item.Title,
                        Description = item.Description,
                        ActionType = item.ActionType,
                        ReferenceId = item.ReferenceId
                    })
                    .ToArray(),
                StoragePolicy = "仪表盘只读取当前用户可见范围内的发票/报关类单据和单一窗口批次汇总；不读取付款/报销单据，不写数据库、不生成文件、不创建默认输出目录或系统 C 盘落点。"
            };
        }

        private static string GetInvoiceStatusText(string status)
        {
            return status switch
            {
                InvoiceStatusCatalog.Draft => "草稿",
                InvoiceStatusCatalog.Verified => "已核对",
                InvoiceStatusCatalog.Shipped => "已出运",
                InvoiceStatusCatalog.Completed => "已结汇",
                InvoiceStatusCatalog.Cancelled => "已作废",
                _ => status ?? string.Empty
            };
        }
    }
}
