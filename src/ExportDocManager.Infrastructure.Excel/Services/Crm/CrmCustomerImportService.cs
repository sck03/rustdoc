using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Crm;

public sealed class CrmCustomerImportService : ICrmCustomerImportService
{
    private const int MaximumRows = 5000;
    private const string PreviewKind = "crm-customers";
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly BusinessDataAccessScope _accessScope;
    private readonly IBusinessClock _clock;

    public CrmCustomerImportService(
        IDbContextFactory<AppDbContext> contextFactory,
        BusinessDataAccessScope accessScope,
        IBusinessClock? clock = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
        _clock = clock ?? BusinessClock.CreateSystem();
    }

    public async Task<CrmCustomerImportPreview> PreviewAsync(
        Stream input,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        IReadOnlyList<IReadOnlyList<string>> table = await TabularImportReader.ReadAsync(
            input,
            fileName,
            MaximumRows,
            cancellationToken);
        if (table.Count < 2)
            throw new InvalidDataException("导入文件至少需要表头和一行客户数据。");

        var columns = BuildColumnMap(table[0]);
        if (!columns.ContainsKey("name"))
            throw new InvalidDataException("导入文件缺少“客户名称”列。");

        var parsedRows = table
            .Skip(1)
            .Select((values, index) => (RowNumber: index + 2, Values: values))
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existingNames = await ScopedExistingNameLoader.LoadAsync(
            _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking()).Select(item => item.Name),
            parsedRows.Select(row => Read(row.Values, columns, "name")),
            cancellationToken);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<CrmCustomerImportRow>(parsedRows.Length);
        foreach (var parsed in parsedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalizedRow normalized = NormalizeRow(parsed.Values, columns);
            string key = PartyImportValidation.CanonicalKey(normalized.Name);
            bool duplicate = key.Length > 0 && (existingNames.Contains(key) || !seenNames.Add(key));
            rows.Add(new CrmCustomerImportRow(
                parsed.RowNumber,
                normalized.Name,
                normalized.CountryRegion,
                normalized.Website,
                normalized.Status,
                normalized.Source,
                normalized.Notes,
                normalized.ContactName,
                normalized.ContactTitle,
                normalized.ContactEmail,
                normalized.ContactPhone,
                duplicate,
                normalized.Error));
        }

        string previewId = await BusinessImportPreviewStore.SaveAsync(
            context,
            _accessScope,
            _clock,
            PreviewKind,
            rows,
            cancellationToken);
        return new CrmCustomerImportPreview(
            rows.Count,
            rows.Count(item => item.Error.Length == 0 && !item.IsDuplicate),
            rows.Count(item => item.IsDuplicate),
            rows,
            previewId);
    }

    public async Task<CrmCustomerImportResult> ImportAsync(
        string previewId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var rows = await BusinessImportPreviewStore.LoadForConsumptionAsync<CrmCustomerImportRow>(
                        context,
                        _accessScope,
                        _clock,
                        PreviewKind,
                        previewId,
                        token);
                    if (rows.Count > MaximumRows)
                    {
                        throw new InfrastructureServiceException("导入预检行数超过服务端上限，数据库内容可能已损坏。");
                    }
                    // Re-normalize every field and deliberately ignore the client
                    // preview flags. Database state may have changed after preview.
                    var normalizedRows = rows
                        .Select(row => NormalizeRow(row))
                        .ToArray();
                    var existingNames = await ScopedExistingNameLoader.LoadAsync(
                        _accessScope.ApplyCrmCustomerScope(context.CrmCustomers.AsNoTracking()).Select(item => item.Name),
                        normalizedRows.Select(row => row.Name),
                        token);
                    int customers = 0;
                    int contacts = 0;
                    int skipped = 0;
                    var pendingContacts = new List<(CrmCustomer Customer, NormalizedRow Row)>();
                    foreach (NormalizedRow row in normalizedRows)
                    {
                        token.ThrowIfCancellationRequested();
                        string key = PartyImportValidation.CanonicalKey(row.Name);
                        if (row.Error.Length > 0 || key.Length == 0 || !existingNames.Add(key))
                        {
                            skipped++;
                            continue;
                        }

                        var customer = new CrmCustomer
                        {
                            Name = row.Name,
                            CountryRegion = row.CountryRegion,
                            Website = row.Website,
                            Status = row.Status,
                            Source = row.Source,
                            Notes = row.Notes,
                            VersionNumber = 1
                        };
                        _accessScope.ApplyOwner(customer);
                        await context.CrmCustomers.AddAsync(customer, token);
                        customers++;
                        if (row.ContactName.Length > 0)
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
                            Name = pending.Row.ContactName,
                            Title = pending.Row.ContactTitle,
                            Email = pending.Row.ContactEmail,
                            Phone = pending.Row.ContactPhone,
                            IsPrimary = true,
                            VersionNumber = 1
                        }, token);
                    }

                    if (pendingContacts.Count > 0)
                        await context.SaveChangesAsync(token);
                    return new CrmCustomerImportResult(customers, contacts, skipped);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new BusinessConcurrencyException("该导入预检已被其他请求提交，请勿重复导入。", exception);
        }
    }

    private static NormalizedRow NormalizeRow(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, int> columns)
    {
        var errors = new List<string>();
        string name = PartyImportValidation.Required(
            Read(values, columns, "name"), 200, "客户名称", errors);
        string country = PartyImportValidation.Text(
            Read(values, columns, "country"), 100, "国家/地区", errors);
        string website = PartyImportValidation.Text(
            Read(values, columns, "website"), 300, "网站", errors);
        string status = NormalizeStatus(
            PartyImportValidation.Text(Read(values, columns, "status"), 30, "状态", errors),
            errors);
        string source = PartyImportValidation.Text(
            Read(values, columns, "source"), 50, "来源", errors);
        string notes = PartyImportValidation.Text(
            Read(values, columns, "notes"), 1000, "备注", errors);
        return NormalizeContact(
            name,
            country,
            website,
            status,
            source,
            notes,
            Read(values, columns, "contact"),
            Read(values, columns, "title"),
            Read(values, columns, "email"),
            Read(values, columns, "phone"),
            errors);
    }

    private static NormalizedRow NormalizeRow(CrmCustomerImportRow row)
    {
        var errors = new List<string>();
        string name = PartyImportValidation.Required(row.Name, 200, "客户名称", errors);
        string country = PartyImportValidation.Text(row.CountryRegion, 100, "国家/地区", errors);
        string website = PartyImportValidation.Text(row.Website, 300, "网站", errors);
        string status = NormalizeStatus(
            PartyImportValidation.Text(row.Status, 30, "状态", errors),
            errors);
        string source = PartyImportValidation.Text(row.Source, 50, "来源", errors);
        string notes = PartyImportValidation.Text(row.Notes, 1000, "备注", errors);
        return NormalizeContact(name, country, website, status, source, notes,
            row.ContactName, row.ContactTitle, row.ContactEmail, row.ContactPhone, errors);
    }

    private static NormalizedRow NormalizeContact(
        string name,
        string country,
        string website,
        string status,
        string source,
        string notes,
        string? contactNameValue,
        string? contactTitleValue,
        string? contactEmailValue,
        string? contactPhoneValue,
        ICollection<string> errors)
    {
        string contactName = PartyImportValidation.Text(contactNameValue, 100, "联系人姓名", errors);
        string contactTitle = PartyImportValidation.Text(contactTitleValue, 100, "联系人职位", errors);
        string contactEmail = PartyImportValidation.Email(contactEmailValue, errors);
        string contactPhone = PartyImportValidation.Text(contactPhoneValue, 100, "联系人电话", errors);
        bool hasContactData = contactName.Length > 0 || contactTitle.Length > 0 ||
                              contactEmail.Length > 0 || contactPhone.Length > 0;
        if (hasContactData && contactName.Length == 0)
        {
            errors.Add("填写联系人职位、邮箱或电话时必须同时填写联系人姓名。");
        }

        return new NormalizedRow(
            name,
            country,
            website,
            status,
            source,
            notes,
            contactName,
            contactTitle,
            contactEmail,
            contactPhone,
            PartyImportValidation.JoinErrors(errors));
    }

    private static string NormalizeStatus(string value, ICollection<string> errors)
    {
        try
        {
            return CrmCustomerStatusCatalog.Normalize(value);
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
            return value;
        }
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["客户名称"] = "name",
            ["公司名称"] = "name",
            ["customername"] = "name",
            ["name"] = "name",
            ["国家地区"] = "country",
            ["国家"] = "country",
            ["countryregion"] = "country",
            ["country"] = "country",
            ["网站"] = "website",
            ["网址"] = "website",
            ["website"] = "website",
            ["状态"] = "status",
            ["status"] = "status",
            ["来源"] = "source",
            ["source"] = "source",
            ["备注"] = "notes",
            ["notes"] = "notes",
            ["联系人"] = "contact",
            ["contactname"] = "contact",
            ["contact"] = "contact",
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
            string normalized = NormalizeHeader(headers[index]);
            if (aliases.TryGetValue(normalized, out string? key) && !result.ContainsKey(key))
                result[key] = index;
        }

        return result;
    }

    private static string Read(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> columns,
        string key) => columns.TryGetValue(key, out int index) && index < row.Count
        ? row[index]
        : string.Empty;

    private static string NormalizeHeader(string? value) =>
        new string((value ?? string.Empty).Normalize(System.Text.NormalizationForm.FormC)
            .Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record NormalizedRow(
        string Name,
        string CountryRegion,
        string Website,
        string Status,
        string Source,
        string Notes,
        string ContactName,
        string ContactTitle,
        string ContactEmail,
        string ContactPhone,
        string Error);
}
