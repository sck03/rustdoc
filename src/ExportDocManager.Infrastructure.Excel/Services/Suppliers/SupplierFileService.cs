using ClosedXML.Excel;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Suppliers;

public sealed class SupplierFileService : ISupplierFileService
{
    private const int MaximumRows = 5000;
    private const int MaximumExportRows = 10000;
    private const string PreviewKind = "suppliers";
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly BusinessDataAccessScope _accessScope;
    private readonly IBusinessClock _clock;

    public SupplierFileService(
        IDbContextFactory<AppDbContext> contextFactory,
        BusinessDataAccessScope accessScope,
        IBusinessClock? clock = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        _clock = clock ?? BusinessClock.CreateSystem();
    }

    public async Task<SupplierImportPreview> PreviewAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var table = await TabularImportReader.ReadAsync(input, fileName, MaximumRows, cancellationToken);
        if (table.Count < 2) throw new InvalidDataException("导入文件至少需要表头和一行供应商数据。");
        var columns = BuildColumns(table[0]);
        if (!columns.ContainsKey("name")) throw new InvalidDataException("导入文件缺少“供应商名称”列。");

        var parsedRows = table.Skip(1)
            .Select((values, index) => (RowNumber: index + 2, Values: values))
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existingNames = await ScopedExistingNameLoader.LoadAsync(
            _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking()).Select(item => item.Name),
            parsedRows.Select(row => Read(row.Values, columns, "name")), cancellationToken);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<SupplierImportRow>(parsedRows.Length);
        foreach (var parsed in parsedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizedRow normalized = NormalizeRow(parsed.Values, columns);
            string key = PartyImportValidation.CanonicalKey(normalized.Name);
            bool duplicate = key.Length > 0 && (existingNames.Contains(key) || !seenNames.Add(key));
            rows.Add(new SupplierImportRow(parsed.RowNumber, normalized.Name, normalized.CountryRegion,
                normalized.Category, normalized.Website, normalized.Status, normalized.MainProducts, normalized.Notes,
                normalized.ContactName, normalized.ContactTitle, normalized.ContactEmail, normalized.ContactPhone,
                duplicate, normalized.Error));
        }

        string previewId = await BusinessImportPreviewStore.SaveAsync(
            context,
            _accessScope,
            _clock,
            PreviewKind,
            rows,
            cancellationToken);
        return new SupplierImportPreview(rows.Count, rows.Count(item => !item.IsDuplicate && item.Error.Length == 0),
            rows.Count(item => item.IsDuplicate), rows, previewId);
    }

    public async Task<SupplierImportResult> ImportAsync(string previewId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await AppDbContextExecution.ExecuteInTransactionAsync(_contextFactory, async (context, token) =>
            {
                var rows = await BusinessImportPreviewStore.LoadForConsumptionAsync<SupplierImportRow>(
                    context,
                    _accessScope,
                    _clock,
                    PreviewKind,
                    previewId,
                    token);
                if (rows.Count > MaximumRows)
                    throw new InfrastructureServiceException("导入预检行数超过服务端上限，数据库内容可能已损坏。");

                var normalizedRows = rows.Select(NormalizeRow).ToArray();
                var existingNames = await ScopedExistingNameLoader.LoadAsync(
                    _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking()).Select(item => item.Name),
                    normalizedRows.Select(row => row.Name), token);
                var pendingContacts = new List<(SupplierCompany Supplier, NormalizedRow Row)>();
                int suppliers = 0, contacts = 0, skipped = 0;
                foreach (NormalizedRow row in normalizedRows)
                {
                    token.ThrowIfCancellationRequested();
                    string key = PartyImportValidation.CanonicalKey(row.Name);
                    // Preview flags are untrusted client input; use this pass as the
                    // sole source of truth for validity and duplicate detection.
                    if (row.Error.Length > 0 || key.Length == 0 || !existingNames.Add(key)) { skipped++; continue; }
                    var supplier = new SupplierCompany
                    {
                        Name = row.Name,
                        CountryRegion = row.CountryRegion,
                        Category = row.Category,
                        Website = row.Website,
                        Status = row.Status,
                        MainProducts = row.MainProducts,
                        Notes = row.Notes,
                        VersionNumber = 1
                    };
                    _accessScope.ApplyOwner(supplier);
                    await context.SupplierCompanies.AddAsync(supplier, token);
                    suppliers++;
                    if (row.ContactName.Length > 0) { pendingContacts.Add((supplier, row)); contacts++; }
                }

                await context.SaveChangesAsync(token);
                foreach (var pending in pendingContacts)
                {
                    await context.SupplierContacts.AddAsync(new SupplierContact
                    {
                        SupplierCompanyId = pending.Supplier.Id,
                        Name = pending.Row.ContactName,
                        Title = pending.Row.ContactTitle,
                        Email = pending.Row.ContactEmail,
                        Phone = pending.Row.ContactPhone,
                        IsPrimary = true,
                        VersionNumber = 1
                    }, token);
                }
                if (pendingContacts.Count > 0) await context.SaveChangesAsync(token);
                return new SupplierImportResult(suppliers, contacts, skipped);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BusinessConcurrencyException("该导入预检已被其他请求提交，请勿重复导入。", exception);
        }
    }

    public async Task<byte[]> ExportAsync(string? keyword, string? status, CancellationToken cancellationToken = default)
    {
        keyword = Clean(keyword); status = Clean(status);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = _accessScope.ApplySupplierScope(context.SupplierCompanies.AsNoTracking());
        query = query.ApplyKeywordSearch(context, keyword, item => item.Name, item => item.Category, item => item.MainProducts);
        if (status.Length > 0) query = query.Where(item => item.Status == status);
        var rows = await query.OrderBy(item => item.Name).Take(MaximumExportRows + 1).Select(item => new
        {
            Supplier = item,
            Contact = context.SupplierContacts.Where(contact => contact.SupplierCompanyId == item.Id)
                .OrderByDescending(contact => contact.IsPrimary).ThenBy(contact => contact.Id).FirstOrDefault()
        }).ToListAsync(cancellationToken);
        if (rows.Count > MaximumExportRows)
            throw new ServiceValidationException($"当前筛选结果超过 {MaximumExportRows:N0} 条。请缩小供应商名称、状态或其它筛选条件后再导出，系统不会静默截断数据。");

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

    private static NormalizedRow NormalizeRow(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> columns)
    {
        var errors = new List<string>();
        string name = PartyImportValidation.Required(Read(values, columns, "name"), 200, "供应商名称", errors);
        string country = PartyImportValidation.Text(Read(values, columns, "country"), 100, "国家/地区", errors);
        string category = PartyImportValidation.Text(Read(values, columns, "category"), 100, "分类", errors);
        string website = PartyImportValidation.Text(Read(values, columns, "website"), 300, "网站", errors);
        string status = NormalizeStatus(PartyImportValidation.Text(Read(values, columns, "status"), 30, "状态", errors), errors);
        string products = PartyImportValidation.Text(Read(values, columns, "products"), 500, "主要产品", errors);
        string notes = PartyImportValidation.Text(Read(values, columns, "notes"), 1000, "备注", errors);
        return NormalizeContact(name, country, category, website, status, products, notes,
            Read(values, columns, "contact"), Read(values, columns, "title"), Read(values, columns, "email"),
            Read(values, columns, "phone"), errors);
    }

    private static NormalizedRow NormalizeRow(SupplierImportRow row)
    {
        var errors = new List<string>();
        string name = PartyImportValidation.Required(row.Name, 200, "供应商名称", errors);
        string country = PartyImportValidation.Text(row.CountryRegion, 100, "国家/地区", errors);
        string category = PartyImportValidation.Text(row.Category, 100, "分类", errors);
        string website = PartyImportValidation.Text(row.Website, 300, "网站", errors);
        string status = NormalizeStatus(PartyImportValidation.Text(row.Status, 30, "状态", errors), errors);
        string products = PartyImportValidation.Text(row.MainProducts, 500, "主要产品", errors);
        string notes = PartyImportValidation.Text(row.Notes, 1000, "备注", errors);
        return NormalizeContact(name, country, category, website, status, products, notes,
            row.ContactName, row.ContactTitle, row.ContactEmail, row.ContactPhone, errors);
    }

    private static NormalizedRow NormalizeContact(string name, string country, string category, string website,
        string status, string products, string notes, string? contactNameValue, string? contactTitleValue,
        string? contactEmailValue, string? contactPhoneValue, ICollection<string> errors)
    {
        string contactName = PartyImportValidation.Text(contactNameValue, 100, "联系人姓名", errors);
        string contactTitle = PartyImportValidation.Text(contactTitleValue, 100, "联系人职位", errors);
        string contactEmail = PartyImportValidation.Email(contactEmailValue, errors);
        string contactPhone = PartyImportValidation.Text(contactPhoneValue, 100, "联系人电话", errors);
        if ((contactName.Length > 0 || contactTitle.Length > 0 || contactEmail.Length > 0 || contactPhone.Length > 0) && contactName.Length == 0)
            errors.Add("填写联系人职位、邮箱或电话时必须同时填写联系人姓名。");
        return new NormalizedRow(name, country, category, website, status, products, notes, contactName,
            contactTitle, contactEmail, contactPhone, PartyImportValidation.JoinErrors(errors));
    }

    private static string NormalizeStatus(string value, ICollection<string> errors)
    {
        try { return SupplierStatusCatalog.Normalize(value); }
        catch (ArgumentException exception) { errors.Add(exception.Message); return value; }
    }

    private static Dictionary<string, int> BuildColumns(IReadOnlyList<string> headers)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["供应商名称"] = "name",
            ["公司名称"] = "name",
            ["suppliername"] = "name",
            ["name"] = "name",
            ["国家地区"] = "country",
            ["国家"] = "country",
            ["country"] = "country",
            ["countryregion"] = "country",
            ["分类"] = "category",
            ["category"] = "category",
            ["网站"] = "website",
            ["website"] = "website",
            ["状态"] = "status",
            ["status"] = "status",
            ["主要产品"] = "products",
            ["产品"] = "products",
            ["mainproducts"] = "products",
            ["备注"] = "notes",
            ["notes"] = "notes",
            ["联系人"] = "contact",
            ["contact"] = "contact",
            ["contactname"] = "contact",
            ["职位"] = "title",
            ["title"] = "title",
            ["邮箱"] = "email",
            ["email"] = "email",
            ["电话"] = "phone",
            ["phone"] = "phone"
        };
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headers.Count; index++)
        {
            string header = new string((headers[index] ?? string.Empty).Normalize(System.Text.NormalizationForm.FormC)
                .Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (aliases.TryGetValue(header, out string? key) && !result.ContainsKey(key)) result[key] = index;
        }
        return result;
    }

    private static string Read(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, string key) =>
        columns.TryGetValue(key, out int index) && index < row.Count ? row[index] : string.Empty;
    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private sealed record NormalizedRow(string Name, string CountryRegion, string Category, string Website,
        string Status, string MainProducts, string Notes, string ContactName, string ContactTitle,
        string ContactEmail, string ContactPhone, string Error);
}
