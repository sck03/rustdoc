using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers
{
    public sealed class SupplierFileService : ISupplierFileService
    {
        private const int MaximumRows = 5000;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public SupplierFileService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<SupplierImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
        {
            var table = await TabularImportReader.ReadAsync(input, fileName, MaximumRows, cancellationToken);
            if (table.Count < 2) throw new InvalidDataException("导入文件至少需要表头和一行供应商数据。");
            var columns = BuildColumns(table[0]);
            if (!columns.ContainsKey("name")) throw new InvalidDataException("导入文件缺少“供应商名称”列。");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var existing = (await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                .Select(item => item.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<SupplierImportRow>();
            foreach (var values in table.Skip(1).Take(MaximumRows))
            {
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                string name = Read(values, columns, "name");
                bool duplicate = name.Length > 0 && (existing.Contains(name) || !seen.Add(name));
                rows.Add(new SupplierImportRow(rows.Count + 2, name, Read(values, columns, "country"),
                    Read(values, columns, "category"), Read(values, columns, "website"),
                    Default(Read(values, columns, "status"), "合作中"), Read(values, columns, "products"),
                    Read(values, columns, "notes"), Read(values, columns, "contact"), Read(values, columns, "title"),
                    Read(values, columns, "email"), Read(values, columns, "phone"), duplicate,
                    name.Length == 0 ? "供应商名称不能为空。" : string.Empty));
            }
            return new SupplierImportPreview(rows.Count, rows.Count(item => !item.IsDuplicate && item.Error.Length == 0),
                rows.Count(item => item.IsDuplicate), rows);
        }

        public Task<SupplierImportResult> ImportAsync(IReadOnlyList<SupplierImportRow> rows, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return AppDbContextExecution.ExecuteInTransactionAsync(_contextFactory, async (context, token) =>
            {
                var existing = (await _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking())
                    .Select(item => item.Name).ToListAsync(token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var pendingContacts = new List<(SupplierCompany Supplier, SupplierImportRow Row)>();
                int suppliers = 0, contacts = 0, skipped = 0;
                foreach (var row in rows.Take(MaximumRows))
                {
                    string name = Clean(row.Name);
                    if (name.Length == 0 || row.Error.Length > 0 || row.IsDuplicate || !existing.Add(name)) { skipped++; continue; }
                    var supplier = new SupplierCompany
                    {
                        Name = name, CountryRegion = Clean(row.CountryRegion), Category = Clean(row.Category),
                        Website = Clean(row.Website), Status = Default(Clean(row.Status), "合作中"),
                        MainProducts = Clean(row.MainProducts), Notes = Clean(row.Notes)
                    };
                    _accessScope.ApplyOwner(supplier);
                    await context.SupplierCompanies.AddAsync(supplier, token);
                    suppliers++;
                    if (Clean(row.ContactName).Length > 0) { pendingContacts.Add((supplier, row)); contacts++; }
                }
                await context.SaveChangesAsync(token);
                foreach (var pending in pendingContacts)
                {
                    await context.SupplierContacts.AddAsync(new SupplierContact
                    {
                        SupplierCompanyId = pending.Supplier.Id, Name = Clean(pending.Row.ContactName),
                        Title = Clean(pending.Row.ContactTitle), Email = Clean(pending.Row.ContactEmail),
                        Phone = Clean(pending.Row.ContactPhone), IsPrimary = true
                    }, token);
                }
                if (pendingContacts.Count > 0) await context.SaveChangesAsync(token);
                return new SupplierImportResult(suppliers, contacts, skipped);
            }, cancellationToken);
        }

        public async Task<byte[]> ExportAsync(string keyword, string status, CancellationToken cancellationToken = default)
        {
            keyword = Clean(keyword); status = Clean(status);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
            if (keyword.Length > 0) query = query.Where(item => item.Name.Contains(keyword) || item.Category.Contains(keyword) || item.MainProducts.Contains(keyword));
            if (status.Length > 0) query = query.Where(item => item.Status == status);
            var rows = await query.OrderBy(item => item.Name).Select(item => new
            {
                Supplier = item,
                Contact = context.SupplierContacts.Where(contact => contact.SupplierCompanyId == item.Id)
                    .OrderByDescending(contact => contact.IsPrimary).ThenBy(contact => contact.Id).FirstOrDefault()
            }).ToListAsync(cancellationToken);
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("供应商");
            string[] headers = ["供应商名称", "国家/地区", "分类", "网站", "状态", "主要产品", "备注", "联系人", "职位", "邮箱", "电话"];
            for (int column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
            for (int index = 0; index < rows.Count; index++)
            {
                int row = index + 2; var item = rows[index];
                object[] values = [item.Supplier.Name, item.Supplier.CountryRegion, item.Supplier.Category, item.Supplier.Website,
                    item.Supplier.Status, item.Supplier.MainProducts, item.Supplier.Notes, item.Contact?.Name ?? string.Empty,
                    item.Contact?.Title ?? string.Empty, item.Contact?.Email ?? string.Empty, item.Contact?.Phone ?? string.Empty];
                for (int column = 0; column < values.Length; column++) sheet.Cell(row, column + 1).Value = values[column]?.ToString() ?? string.Empty;
            }
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
            using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
        }

        private static Dictionary<string, int> BuildColumns(IReadOnlyList<string> headers)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["供应商名称"]="name", ["公司名称"]="name", ["suppliername"]="name", ["name"]="name",
                ["国家地区"]="country", ["国家"]="country", ["country"]="country", ["countryregion"]="country",
                ["分类"]="category", ["category"]="category", ["网站"]="website", ["website"]="website",
                ["状态"]="status", ["status"]="status", ["主要产品"]="products", ["产品"]="products", ["mainproducts"]="products",
                ["备注"]="notes", ["notes"]="notes", ["联系人"]="contact", ["contact"]="contact", ["contactname"]="contact",
                ["职位"]="title", ["title"]="title", ["邮箱"]="email", ["email"]="email", ["电话"]="phone", ["phone"]="phone"
            };
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < headers.Count; index++)
            {
                string header = new string((headers[index] ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (aliases.TryGetValue(header, out string key) && !result.ContainsKey(key)) result[key] = index;
            }
            return result;
        }
        private static string Read(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string key) => columns.TryGetValue(key, out int index) && index < row.Count ? Clean(row[index]) : string.Empty;
        private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
