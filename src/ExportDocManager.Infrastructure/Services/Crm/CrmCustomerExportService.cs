using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Crm
{
    public sealed class CrmCustomerExportService : ICrmCustomerExportService
    {
        private const int MaximumRows = 10000;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public CrmCustomerExportService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<byte[]> ExportAsync(string keyword, string status, CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword);
            status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var customers = _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking());
            customers = customers.ApplyKeywordSearch(
                keyword,
                item => item.Name,
                item => item.CountryRegion,
                item => item.Website,
                item => item.Source,
                item => item.Notes);
            if (status.Length > 0) customers = customers.Where(item => item.Status == status);
            var rows = await customers.OrderBy(item => item.Name).Take(MaximumRows + 1)
                .Select(item => new
                {
                    Customer = item,
                    Contact = context.CrmContacts.Where(contact => contact.CrmCustomerId == item.Id)
                        .OrderByDescending(contact => contact.IsPrimary).ThenBy(contact => contact.Id).FirstOrDefault()
                }).ToListAsync(cancellationToken);
            if (rows.Count > MaximumRows)
            {
                throw new ServiceValidationException(
                    $"当前筛选结果超过 {MaximumRows:N0} 条。请缩小客户名称、状态或其它筛选条件后再导出，系统不会静默截断数据。");
            }

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("CRM客户");
            string[] headers = ["客户名称", "国家/地区", "网站", "状态", "来源", "备注", "主要联系人", "职位", "邮箱", "电话", "即时通讯"];
            for (int column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
            for (int index = 0; index < rows.Count; index++)
            {
                var row = rows[index]; int line = index + 2;
                sheet.Cell(line, 1).Value = row.Customer.Name;
                sheet.Cell(line, 2).Value = row.Customer.CountryRegion;
                sheet.Cell(line, 3).Value = row.Customer.Website;
                sheet.Cell(line, 4).Value = row.Customer.Status;
                sheet.Cell(line, 5).Value = row.Customer.Source;
                sheet.Cell(line, 6).Value = row.Customer.Notes;
                sheet.Cell(line, 7).Value = row.Contact?.Name ?? string.Empty;
                sheet.Cell(line, 8).Value = row.Contact?.Title ?? string.Empty;
                sheet.Cell(line, 9).Value = row.Contact?.Email ?? string.Empty;
                sheet.Cell(line, 10).Value = row.Contact?.Phone ?? string.Empty;
                sheet.Cell(line, 11).Value = row.Contact?.InstantMessaging ?? string.Empty;
            }
            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 300));
            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return output.ToArray();
        }

        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
