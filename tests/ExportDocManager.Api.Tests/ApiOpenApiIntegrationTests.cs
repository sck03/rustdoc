using System.Net;
using System.Text.Json;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests;

public sealed class ApiOpenApiIntegrationTests
{
    private static readonly IReadOnlyDictionary<string, (bool Bearer, bool Desktop)> ExplicitSecurity =
        new Dictionary<string, (bool Bearer, bool Desktop)>(StringComparer.OrdinalIgnoreCase)
        {
            ["POST /api/auth/login"] = (false, true),
            ["POST /api/system/shutdown-maintenance"] = (false, true),
            ["GET /downloads/jobs/{token}"] = (false, true),
            ["GET /downloads/postgresql-backups/{token}"] = (false, true),
            ["GET /api/system/license"] = (true, true),
            ["POST /api/system/license/register"] = (true, true)
        };
    private static readonly HashSet<string> BodylessSuccessOperations = new(StringComparer.Ordinal)
    {
        "GET /livez",
        "HEAD /livez",
        "GET /readyz",
        "HEAD /readyz",
        "DELETE /api/master-data/hs-knowledge/examples/{id}"
    };

    private static readonly string[] RequiredBusinessPaths =
    {
        "/api/auth/login",
        "/api/auth/me",
        "/api/invoices",
        "/api/payments",
        "/api/settings",
        "/api/backup",
        "/api/postgresql-maintenance/backups/upload-restore",
        "/api/server-migration/restore",
        "/api/reports/templates",
        "/api/master-data/customers",
        "/api/master-data/hs-codes",
        "/api/single-window/reference-catalog",
        "/api/single-window/operation-center",
        "/api/single-window/coo/{invoiceId}",
        "/api/tools/excel/import-preview",
        "/api/tools/pdf/merge/upload"
    };

    [Fact]
    public async Task OfficialOpenApiDocument_ShouldBeTheCompleteContractSource()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("api-openapi", "openapi.db");
        using var client = harness.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement paths = root.GetProperty("paths");

        Assert.StartsWith("3.1.", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("ExportDocManager API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.False(paths.TryGetProperty("/swagger", out _));
        foreach (string path in RequiredBusinessPaths)
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Official OpenAPI is missing required path: {path}");
        }

        AssertSecuritySchemes(root);
        AssertSameOriginServer(root);
        AssertOperations(root, paths, desktopAccessEnabled: false);
        AssertLocalReferencesResolve(root, root, "$");
        AssertCriticalRequestParameters(paths);
        AssertTypedEndpointResponseContracts(paths);
    }

    [Fact]
    public async Task SwaggerCompatibilityPath_ShouldServeTheOfficialV1Document()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("api-swagger-compat", "swagger.db");
        using var client = harness.CreateClient();

        using var officialResponse = await client.GetAsync("/openapi/v1.json");
        using var compatibilityResponse = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, officialResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, compatibilityResponse.StatusCode);
        using var official = JsonDocument.Parse(await officialResponse.Content.ReadAsStreamAsync());
        using var compatibility = JsonDocument.Parse(await compatibilityResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            official.RootElement.GetProperty("paths").EnumerateObject().Select(item => item.Name).Order(),
            compatibility.RootElement.GetProperty("paths").EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal(
            official.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(item => item.Name).Order(),
            compatibility.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(item => item.Name).Order());
    }

    [Fact]
    public async Task OfficialOpenApiDocument_ShouldDeclareDesktopAccessOnlyWhenEnabled()
    {
        const string desktopToken = "openapi-desktop-token";
        await using var harness = await ApiIntegrationTestHarness.StartAsync(
            "api-openapi-desktop",
            "openapi-desktop.db",
            desktopAccessToken: desktopToken);
        using var client = harness.CreateClient(desktopAccessToken: desktopToken);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        AssertOperations(root, root.GetProperty("paths"), desktopAccessEnabled: true);
    }

    [Fact]
    public async Task OfficialOpenApiDocument_ShouldPublishConfiguredPathBase()
    {
        await using var harness = await ApiIntegrationTestHarness.StartAsync("api-openapi-path-base", "openapi-path-base.db", pathBase: "/exportdoc");
        using var response = await harness.CreateClient().GetAsync("/exportdoc/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("/exportdoc", Assert.Single(document.RootElement.GetProperty("servers").EnumerateArray())
            .GetProperty("url").GetString());
    }

    private static void AssertSecuritySchemes(JsonElement root)
    {
        JsonElement schemes = root.GetProperty("components").GetProperty("securitySchemes");
        JsonElement bearer = schemes.GetProperty("BearerAuth");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        JsonElement desktop = schemes.GetProperty("DesktopAccess");
        Assert.Equal("apiKey", desktop.GetProperty("type").GetString());
        Assert.Equal("header", desktop.GetProperty("in").GetString());
        Assert.Equal(ApiDesktopAccessOptions.HeaderName, desktop.GetProperty("name").GetString());
    }

    private static void AssertSameOriginServer(JsonElement root)
    {
        JsonElement server = Assert.Single(root.GetProperty("servers").EnumerateArray());
        Assert.Equal("/", server.GetProperty("url").GetString());
    }

    private static void AssertOperations(
        JsonElement root,
        JsonElement paths,
        bool desktopAccessEnabled)
    {
        var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int operationCount = 0;
        foreach (JsonProperty path in paths.EnumerateObject())
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                if (!IsHttpMethod(method.Name))
                {
                    continue;
                }

                operationCount++;
                JsonElement operation = method.Value;
                string operationKey = $"{method.Name.ToUpperInvariant()} {path.Name}";
                string operationId = operation.GetProperty("operationId").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(operationId), $"{operationKey} has no operationId.");
                if (!method.NameEquals("head"))
                {
                    Assert.True(operationIds.Add(operationId), $"Duplicate operationId: {operationId}");
                }

                JsonElement responses = operation.GetProperty("responses");
                JsonProperty[] successes = responses.EnumerateObject()
                    .Where(item => item.Name.Length == 3 && item.Name[0] == '2')
                    .ToArray();
                Assert.NotEmpty(successes);
                if (!BodylessSuccessOperations.Contains(operationKey))
                {
                    Assert.Contains(successes, success => ResponseHasSchema(success.Value));
                }

                AssertPathParameters(path.Name, operationKey, operation);
                AssertRequestBody(operationKey, operation);
                AssertOperationSecurity(operationKey, operation, desktopAccessEnabled);
            }
        }

        Assert.True(operationCount >= 320, $"Unexpected OpenAPI operation count: {operationCount}");
        Assert.Equal(operationCount - 2, operationIds.Count);
        Assert.True(root.GetProperty("components").GetProperty("schemas").EnumerateObject().Any());
    }

    private static void AssertOperationSecurity(
        string operationKey,
        JsonElement operation,
        bool desktopAccessEnabled)
    {
        JsonElement[] requirements = operation.TryGetProperty("security", out var security)
            ? security.EnumerateArray().ToArray()
            : [];
        bool isAnonymousInfrastructure = operationKey.StartsWith("GET /healthz", StringComparison.OrdinalIgnoreCase)
            || operationKey.StartsWith("GET /livez", StringComparison.OrdinalIgnoreCase)
            || operationKey.StartsWith("HEAD /livez", StringComparison.OrdinalIgnoreCase)
            || operationKey.StartsWith("GET /readyz", StringComparison.OrdinalIgnoreCase)
            || operationKey.StartsWith("HEAD /readyz", StringComparison.OrdinalIgnoreCase);
        var expected = ExplicitSecurity.TryGetValue(operationKey, out var explicitSecurity)
            ? explicitSecurity
            : isAnonymousInfrastructure
                ? (Bearer: false, Desktop: false)
                : (Bearer: true, Desktop: true);
        bool requiresBearer = expected.Bearer;
        bool requiresDesktop = desktopAccessEnabled && expected.Desktop;
        if (!requiresBearer && !requiresDesktop)
        {
            Assert.True(requirements.Length == 0, $"{operationKey} unexpectedly declares security.");
            return;
        }

        JsonElement requirement = Assert.Single(requirements);
        Assert.True(
            requiresBearer == requirement.TryGetProperty("BearerAuth", out _),
            $"{operationKey} bearer metadata mismatch.");
        Assert.True(
            requiresDesktop == requirement.TryGetProperty("DesktopAccess", out _),
            $"{operationKey} desktop metadata mismatch.");
    }

    private static void AssertPathParameters(string path, string operationKey, JsonElement operation)
    {
        string[] placeholders = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}'))
            .Select(segment => segment[1..^1].Split(':')[0])
            .ToArray();
        if (placeholders.Length == 0)
        {
            return;
        }

        JsonElement[] parameters = operation.TryGetProperty("parameters", out JsonElement parameterDocument)
            ? parameterDocument.EnumerateArray().ToArray()
            : [];
        foreach (string placeholder in placeholders)
        {
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("in").GetString() == "path"
                && parameter.GetProperty("name").GetString() == placeholder
                && parameter.GetProperty("required").GetBoolean());
        }
    }

    private static void AssertRequestBody(string operationKey, JsonElement operation)
    {
        if (!operation.TryGetProperty("requestBody", out JsonElement requestBody))
        {
            return;
        }

        Assert.True(requestBody.GetProperty("required").GetBoolean(), $"{operationKey} has an optional request body.");
        Assert.Contains(requestBody.GetProperty("content").EnumerateObject(), content =>
            content.Value.TryGetProperty("schema", out _));
    }

    private static void AssertCriticalRequestParameters(JsonElement paths)
    {
        AssertParameters(paths, "/api/auth/login", "post", (ApiRuntimeOptions.BootstrapTokenHeaderName, "header", false));
        AssertParameters(
            paths,
            "/api/postgresql-maintenance/backups/upload-restore",
            "post",
            (ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader, "header", true),
            (ApiEndpointRouteBuilderExtensions.SensitiveOperationTicketHeader, "header", true),
            (ApiEndpointRouteBuilderExtensions.PostgreSqlBackupFileNameHeader, "header", true));
        AssertParameters(
            paths,
            "/api/server-migration/restore",
            "post",
            (ApiEndpointRouteBuilderExtensions.RestoreConfirmationHeader, "header", true),
            (ApiEndpointRouteBuilderExtensions.SensitiveOperationTicketHeader, "header", true),
            (ApiEndpointRouteBuilderExtensions.ServerMigrationPasswordHeader, "header", true),
            (ApiEndpointRouteBuilderExtensions.ServerMigrationFileNameHeader, "header", true));
        AssertParameters(
            paths,
            "/api/single-window/reference-catalog/excel/preview",
            "post",
            ("codeColumn", "query", false),
            ("aliasesColumn", "query", false));
    }

    private static void AssertParameters(
        JsonElement paths,
        string path,
        string method,
        params (string Name, string Location, bool Required)[] expected)
    {
        JsonElement[] parameters = paths.GetProperty(path).GetProperty(method)
            .GetProperty("parameters").EnumerateArray().ToArray();
        foreach (var item in expected)
        {
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("name").GetString() == item.Name
                && parameter.GetProperty("in").GetString() == item.Location
                && (parameter.TryGetProperty("required", out JsonElement required)
                    && required.GetBoolean()) == item.Required);
        }
    }

    private static void AssertTypedEndpointResponseContracts(JsonElement paths)
    {
        AssertResponseSchema(paths, "/api/auth/login", "post", "200", "ApiLoginResponse");
        AssertResponseSchema(paths, "/api/auth/login", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/auth/login", "post", "401", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/auth/login", "post", "429", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/auth/me", "get", "200", "ApiUserDto");
        AssertResponseSchema(paths, "/api/auth/logout", "post", "200", "ApiLogoutResponse");

        AssertResponseSchema(paths, "/api/system/license", "get", "200", "ApiLicenseStatusResponse");
        AssertResponseSchema(paths, "/api/system/license/register", "post", "200", "ApiLicenseRegisterResponse");
        AssertResponseSchema(paths, "/api/system/license/register", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/system/license/register", "post", "403", "ApiErrorResponse");

        AssertResponseSchema(paths, "/api/settings", "get", "200", "ApiSettingsResponse");
        AssertResponseSchema(paths, "/api/settings/validate", "post", "200", "ApiSettingsValidationResponse");
        AssertResponseSchema(paths, "/api/settings/validate", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/settings/validate", "post", "403", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/settings", "put", "200", "ApiSettingsSaveResponse");
        AssertResponseSchema(paths, "/api/settings", "put", "400", "ApiErrorResponse");

        AssertResponseSchema(paths, "/api/dashboard", "get", "200", "ApiDashboardResponse");
        AssertResponseSchema(paths, "/api/payments", "get", "200", "ApiPagedResponseOfApiPaymentDto");
        AssertResponseSchema(paths, "/api/payments/{id}", "get", "200", "ApiPaymentDto");
        AssertResponseSchema(paths, "/api/payments", "post", "201", "ApiPaymentSaveResponse");
        AssertResponseSchema(paths, "/api/payments/{id}", "put", "200", "ApiPaymentSaveResponse");
        AssertResponseSchema(paths, "/api/payments/{id}", "delete", "200", "ApiCommandResponse");
        AssertArrayResponseItemSchema(paths, "/api/email-templates", "get", "200", "ApiEmailTemplateDto");
        AssertArrayResponseItemSchema(
            paths,
            "/api/email-templates/variables",
            "get",
            "200",
            "ApiEmailTemplateVariableDto");
        AssertResponseSchema(paths, "/api/email-templates/preview", "post", "200", "ApiEmailTemplatePreviewDto");
        AssertResponseSchema(paths, "/api/email-templates", "post", "201", "ApiEmailTemplateDto");
        AssertResponseSchema(paths, "/api/email-templates", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/email-templates", "post", "409", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/email-templates/{id}", "put", "200", "ApiEmailTemplateDto");
        AssertArrayResponseItemSchema(
            paths,
            "/api/email-templates/{id}/versions",
            "get",
            "200",
            "ApiEmailTemplateVersionDto");
        AssertResponseSchema(
            paths,
            "/api/email-templates/{id}/versions/{versionNumber}/restore",
            "post",
            "200",
            "ApiEmailTemplateDto");
        AssertResponseSchema(paths, "/api/email-templates/{id}", "delete", "200", "ApiCommandResponse");
        AssertResponseSchema(
            paths,
            "/api/crm/opportunities",
            "get",
            "200",
            "ApiPagedResponseOfApiSalesOpportunityDto");
        AssertResponseSchema(paths, "/api/crm/opportunities", "post", "201", "ApiSalesOpportunityDto");
        AssertResponseSchema(paths, "/api/crm/opportunities", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/crm/opportunities", "post", "409", "ApiErrorResponse");
        AssertArrayResponseItemSchema(
            paths,
            "/api/crm/opportunities/{id}/history",
            "get",
            "200",
            "ApiSalesOpportunityHistoryDto");
        AssertResponseSchema(paths, "/api/crm/opportunities/{id}", "put", "200", "ApiSalesOpportunityDto");
        AssertResponseSchema(paths, "/api/crm/opportunities/{id}", "delete", "200", "ApiCommandResponse");
        AssertResponseSchema(paths, "/api/users", "get", "200", "ApiUserListResponse");
        AssertResponseSchema(paths, "/api/users", "get", "403", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/users", "post", "200", "ApiUserSaveResponse");
        AssertResponseSchema(paths, "/api/users", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/users", "post", "409", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/users/{id}", "put", "200", "ApiUserSaveResponse");
        AssertResponseSchema(paths, "/api/users/{id}", "delete", "200", "ApiCommandResponse");
        AssertResponseSchema(paths, "/api/permission-templates", "get", "200", "ApiPermissionTemplateCatalogResponse");
        AssertResponseSchema(paths, "/api/permission-templates", "get", "403", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/permission-templates", "post", "200", "ApiPermissionTemplateDto");
        AssertResponseSchema(paths, "/api/permission-templates", "post", "400", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/permission-templates", "post", "409", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/permission-templates/{id}", "put", "200", "ApiPermissionTemplateDto");
        AssertResponseSchema(paths, "/api/permission-templates/{id}", "delete", "200", "ApiCommandResponse");
        AssertResponseSchema(
            paths,
            "/api/system/data-maintenance/invoices/{id}",
            "get",
            "200",
            "ApiInvoiceDataMaintenancePreviewResponse");
        AssertResponseSchema(
            paths,
            "/api/system/data-maintenance/invoices/{id}",
            "get",
            "403",
            "ApiErrorResponse");
        AssertResponseSchema(
            paths,
            "/api/system/data-maintenance/invoices/{id}/purge",
            "post",
            "200",
            "ApiInvoicePurgeResponse");
        AssertResponseSchema(
            paths,
            "/api/system/data-maintenance/invoices/{id}/purge",
            "post",
            "409",
            "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/query/invoices", "get", "200", "ApiPagedResponseOfApiQueryInvoiceRowDto");
        AssertArrayResponseItemSchema(paths, "/api/master-data/customers", "get", "200", "ApiCustomerDto");
        AssertResponseSchema(paths, "/api/master-data/customers/page", "get", "200", "ApiPagedResponseOfApiCustomerDto");
        AssertResponseSchema(paths, "/api/master-data/exporters/page", "get", "200", "ApiPagedResponseOfApiExporterDto");
        AssertResponseSchema(paths, "/api/master-data/exporters/{id}/seals/{sealType}/upload", "post", "200", "ApiExporterDto");
        AssertResponseSchema(paths, "/api/master-data/exporters/{id}/seals/{sealType}/upload", "post", "413", "ApiErrorResponse");
        AssertResponseSchema(paths, "/api/master-data/payees/page", "get", "200", "ApiPagedResponseOfApiPayeeDto");
        AssertResponseSchema(paths, "/api/master-data/ports/page", "get", "200", "ApiPagedResponseOfApiPortDto");
        AssertResponseSchema(paths, "/api/master-data/products", "get", "200", "ApiPagedResponseOfApiProductDto");
        AssertResponseSchema(paths, "/api/master-data/units/page", "get", "200", "ApiPagedResponseOfApiUnitDto");

        AssertResponseSchema(paths, "/api/custom-options/{optionType}", "get", "200", "ApiCustomOptionListResponse");
        AssertResponseSchema(paths, "/api/custom-options/{optionType}", "post", "200", "ApiCustomOptionListResponse");
        AssertResponseSchema(
            paths,
            "/api/single-window/coo/producer-profiles",
            "get",
            "200",
            "ApiCustomsCooProducerProfileListResponse");
        AssertResponseSchema(
            paths,
            "/api/single-window/coo/producer-profiles/{id}",
            "get",
            "200",
            "ApiCustomsCooProducerProfileResponse");
        AssertResponseSchema(
            paths,
            "/api/single-window/operation-center",
            "get",
            "200",
            "SingleWindowOperationCenterPageResult");
    }

    private static void AssertResponseSchema(
        JsonElement paths,
        string path,
        string method,
        string status,
        string schemaName)
    {
        JsonElement response = paths.GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status);
        string reference = response.GetProperty("content").EnumerateObject().First().Value
            .GetProperty("schema").GetProperty("$ref").GetString() ?? string.Empty;
        Assert.EndsWith($"/{schemaName}", reference, StringComparison.Ordinal);
    }

    private static void AssertArrayResponseItemSchema(
        JsonElement paths,
        string path,
        string method,
        string status,
        string itemSchemaName)
    {
        JsonElement schema = paths.GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status)
            .GetProperty("content").EnumerateObject().First().Value
            .GetProperty("schema");
        Assert.Equal("array", schema.GetProperty("type").GetString());
        string reference = schema.GetProperty("items").GetProperty("$ref").GetString() ?? string.Empty;
        Assert.EndsWith($"/{itemSchemaName}", reference, StringComparison.Ordinal);
    }

    private static bool ResponseHasSchema(JsonElement response) =>
        response.TryGetProperty("content", out JsonElement content)
        && content.EnumerateObject().Any(item => item.Value.TryGetProperty("schema", out _));

    private static bool IsHttpMethod(string value) => value is
        "get" or "post" or "put" or "delete" or "patch" or "head" or "options" or "trace";

    private static void AssertLocalReferencesResolve(JsonElement root, JsonElement current, string location)
    {
        if (current.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in current.EnumerateObject())
            {
                string propertyLocation = $"{location}.{property.Name}";
                if (property.NameEquals("$ref")
                    && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is string reference
                    && reference.StartsWith("#/", StringComparison.Ordinal))
                {
                    Assert.True(TryResolveJsonPointer(root, reference),
                        $"Unresolved OpenAPI reference at {propertyLocation}: {reference}");
                }

                AssertLocalReferencesResolve(root, property.Value, propertyLocation);
            }
            return;
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in current.EnumerateArray())
            {
                AssertLocalReferencesResolve(root, item, $"{location}[{index++}]");
            }
        }
    }

    private static bool TryResolveJsonPointer(JsonElement root, string reference)
    {
        JsonElement current = root;
        foreach (string encodedSegment in reference[2..].Split('/'))
        {
            string segment = Uri.UnescapeDataString(encodedSegment)
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out JsonElement property))
            {
                current = property;
                continue;
            }

            if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, out int index)
                && index >= 0
                && index < current.GetArrayLength())
            {
                current = current[index];
                continue;
            }

            return false;
        }
        return true;
    }
}
