using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Crm
{
    public sealed class CrmCustomerImportService : ICrmCustomerImportService
    {
        private const int MaximumRows = 5000;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly BusinessDataAccessScope _accessScope;

        public CrmCustomerImportService(IDbContextFactory<AppDbContext> contextFactory, BusinessDataAccessScope accessScope)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        }

        public async Task<CrmCustomerImportPreview> PreviewAsync(
            Stream input, string fileName, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            IReadOnlyList<IReadOnlyList<string>> table = await TabularImportReader.ReadAsync(input, fileName, MaximumRows, cancellationToken);
            if (table.Count < 2) throw new InvalidDataException("导入文件至少需要表头和一行客户数据。");

            var columns = BuildColumnMap(table[0]);
            if (!columns.ContainsKey("name")) throw new InvalidDataException("导入文件缺少“客户名称”列。");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var existingNames = (await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                    .Select(item => item.Name).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<CrmCustomerImportRow>();
            foreach (var values in table.Skip(1).Take(MaximumRows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int rowNumber = rows.Count + 2;
                string name = Read(values, columns, "name");
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                string error = name.Length == 0 ? "客户名称不能为空。" : string.Empty;
                bool duplicate = name.Length > 0 && (existingNames.Contains(name) || !seenNames.Add(name));
                rows.Add(new CrmCustomerImportRow(
                    rowNumber, name, Read(values, columns, "country"), Read(values, columns, "website"),
                    Default(Read(values, columns, "status"), "潜在客户"), Read(values, columns, "source"),
                    Read(values, columns, "notes"), Read(values, columns, "contact"), Read(values, columns, "title"),
                    Read(values, columns, "email"), Read(values, columns, "phone"), duplicate, error));
            }

            return new CrmCustomerImportPreview(
                rows.Count,
                rows.Count(item => item.Error.Length == 0 && !item.IsDuplicate),
                rows.Count(item => item.IsDuplicate),
                rows);
        }

        public Task<CrmCustomerImportResult> ImportAsync(
            IReadOnlyList<CrmCustomerImportRow> rows, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(rows);
            return AppDbContextExecution.ExecuteInTransactionAsync(_contextFactory, async (context, token) =>
            {
                var existingNames = (await _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking())
                        .Select(item => item.Name).ToListAsync(token))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                int customers = 0;
                int contacts = 0;
                int skipped = 0;
                var pendingContacts = new List<(CrmCustomer Customer, CrmCustomerImportRow Row)>();
                foreach (var row in rows.Take(MaximumRows))
                {
                    token.ThrowIfCancellationRequested();
                    string name = Clean(row.Name);
                    if (row.Error.Length > 0 || name.Length == 0 || row.IsDuplicate || !existingNames.Add(name))
                    {
                        skipped++;
                        continue;
                    }

                    var customer = new CrmCustomer
                    {
                        Name = name,
                        CountryRegion = Clean(row.CountryRegion),
                        Website = Clean(row.Website),
                        Status = Default(Clean(row.Status), "潜在客户"),
                        Source = Clean(row.Source),
                        Notes = Clean(row.Notes)
                    };
                    _accessScope.ApplyOwner(customer);
                    await context.CrmCustomers.AddAsync(customer, token);
                    customers++;
                    if (Clean(row.ContactName).Length > 0)
                    {
                        pendingContacts.Add((customer, row));
                        contacts++;
                    }
                }
                await context.SaveChangesAsync(token);
                foreach (var pending in pendingContacts)
                {
                    await context.CrmContacts.AddAsync(new CrmContact
                    {
                        CrmCustomerId = pending.Customer.Id,
                        Name = Clean(pending.Row.ContactName),
                        Title = Clean(pending.Row.ContactTitle),
                        Email = Clean(pending.Row.ContactEmail),
                        Phone = Clean(pending.Row.ContactPhone),
                        IsPrimary = true
                    }, token);
                }
                if (pendingContacts.Count > 0) await context.SaveChangesAsync(token);
                return new CrmCustomerImportResult(customers, contacts, skipped);
            }, cancellationToken);
        }

        private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["客户名称"]="name", ["公司名称"]="name", ["customername"]="name", ["name"]="name",
                ["国家地区"]="country", ["国家"]="country", ["countryregion"]="country", ["country"]="country",
                ["网站"]="website", ["网址"]="website", ["website"]="website",
                ["状态"]="status", ["status"]="status", ["来源"]="source", ["source"]="source",
                ["备注"]="notes", ["notes"]="notes", ["联系人"]="contact", ["contactname"]="contact", ["contact"]="contact",
                ["职位"]="title", ["title"]="title", ["邮箱"]="email", ["email"]="email", ["电话"]="phone", ["phone"]="phone"
            };
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < headers.Count; index++)
            {
                string normalized = NormalizeHeader(headers[index]);
                if (aliases.TryGetValue(normalized, out string key) && !result.ContainsKey(key)) result[key] = index;
            }
            return result;
        }

        private static string Read(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string key) =>
            columns.TryGetValue(key, out int index) && index < row.Count ? Clean(row[index]) : string.Empty;
        private static string NormalizeHeader(string value) => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
        private static string Clean(string value) => (value ?? string.Empty).Trim();
    }
}
