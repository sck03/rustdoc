using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiEndpointAuth
    {
        public const string AuthenticatedUserItemKey = "__ExportDocManagerApiUser";

        public static User RequireUser(HttpContext context, IApiSessionTokenService tokenService)
        {
            ArgumentNullException.ThrowIfNull(tokenService);
            return ApiCurrentUserResolver.ResolveCachedUser(context);
        }

        public static bool HasValidDesktopAccess(HttpContext context, ApiDesktopAccessOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            string submittedToken = context.Request.Headers[ApiDesktopAccessOptions.HeaderName].ToString();
            return options.IsValid(submittedToken);
        }
    }

    public sealed class ApiAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ApiCurrentUserResolver currentUserResolver)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(currentUserResolver);

            if (!RequiresAuthentication(context.Request.Path))
            {
                await _next(context);
                return;
            }

            if (await currentUserResolver.ResolveAsync(context, context.RequestAborted).ConfigureAwait(false) == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
        }

        public static bool RequiresAuthentication(PathString path)
        {
            if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/api/system/shutdown-maintenance", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ApiDesktopAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiDesktopAccessMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ApiDesktopAccessOptions options)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(options);

            if (!options.IsEnabled || !RequiresDesktopAccess(context.Request.Path))
            {
                await _next(context);
                return;
            }

            string submittedToken = context.Request.Headers[ApiDesktopAccessOptions.HeaderName].ToString();
            if (!options.IsValid(submittedToken))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await _next(context);
        }

        public static bool RequiresDesktopAccess(PathString path)
        {
            return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ApiLicenseRequirementMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiLicenseRequirementMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(HttpContext context, ILicenseService licenseService)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(licenseService);

            if (!RequiresValidLicense(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var status = await licenseService.GetStatusAsync(context.RequestAborted).ConfigureAwait(false);
            if (!status.IsTrialExpired)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
            await context.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(string.IsNullOrWhiteSpace(status.Message)
                        ? "试用期已过，请先注册授权。"
                        : status.Message),
                    cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
        }

        public static bool RequiresValidLicense(PathString path)
        {
            if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/api/system/license", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/api/system/license/register", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("/api/system/shutdown-maintenance", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ApiWorkspaceAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiWorkspaceAccessMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task InvokeAsync(
            HttpContext context,
            ApiCurrentUserResolver currentUserResolver,
            ApiAuthorizationService authorizationService)
        {
            var requiredModule = GetRequiredModule(context.Request.Path, context.Request.Method, context.Request.Query);
            if (requiredModule == null)
            {
                await _next(context);
                return;
            }

            var user = currentUserResolver.ResolveCached(context);
            string requiredAccessLevel = GetRequiredAccessLevel(context.Request.Method);
            if (!authorizationService.CanUseModule(user, requiredModule, requiredAccessLevel))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await _next(context);
        }

        public static string GetRequiredWorkspace(PathString path)
        {
            if (path.StartsWithSegments("/api/crm", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/api/suppliers", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/api/email-templates", StringComparison.OrdinalIgnoreCase))
            {
                return ProductEditionCatalog.Sales;
            }

            string[] documentPrefixes =
            [
                "/api/invoices",
                "/api/dashboard",
                "/api/query",
                "/api/payments",
                "/api/master-data",
                "/api/custom-options",
                "/api/single-window",
                "/api/reports",
                "/api/jobs",
                "/api/tools/excel",
                "/api/tools/ocr",
                "/api/tools/container-packing",
                "/api/tools/letter-of-credit",
                "/api/tools/pdf"
            ];

            return documentPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                ? ProductEditionCatalog.Document
                : null;
        }

        public static string GetRequiredModule(PathString path)
        {
            return GetRequiredModule(path, HttpMethods.Get);
        }

        public static string GetRequiredModule(PathString path, string method)
        {
            return GetRequiredModule(path, method, null);
        }

        public static string GetRequiredModule(
            PathString path,
            string method,
            IQueryCollection query)
        {
            if (path.StartsWithSegments("/api/crm/opportunities", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.SalesOpportunities;
            if (path.StartsWithSegments("/api/crm/dashboard", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.SalesDashboard;
            if (path.StartsWithSegments("/api/crm", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.SalesCrm;
            if (path.StartsWithSegments("/api/email-templates", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.SalesEmailTemplates;
            if (path.StartsWithSegments("/api/suppliers", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.SalesSuppliers;
            if (path.StartsWithSegments("/api/dashboard", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentDashboard;
            if (path.StartsWithSegments("/api/invoices", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentInvoices;
            if (path.StartsWithSegments("/api/query", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentQuery;
            if (path.StartsWithSegments("/api/payments", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentPayments;
            if (path.StartsWithSegments("/api/jobs", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentJobs;
            if ((path.StartsWithSegments("/api/master-data/customers", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWithSegments("/api/master-data/payees", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWithSegments("/api/master-data/exporters", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWithSegments("/api/master-data/units", StringComparison.OrdinalIgnoreCase)) &&
                (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
                return PermissionModuleCatalog.DocumentReferenceData;
            if (path.StartsWithSegments("/api/master-data/products", StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
                return PermissionModuleCatalog.CommonProductReference;
            if (path.StartsWithSegments("/api/master-data", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentMasterData;
            if (path.StartsWithSegments("/api/custom-options", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentCustomOptions;
            if (path.StartsWithSegments("/api/single-window", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentSingleWindow;
            if (path.StartsWithSegments("/api/reports/payments", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentPaymentReports;
            if (path.StartsWithSegments("/api/reports/invoices", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentInvoiceReports;
            if (path.Equals("/api/reports/templates", StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) &&
                IsPaymentReportType(query?["reportType"].ToString()))
                return PermissionModuleCatalog.DocumentPaymentReports;
            if (path.Equals("/api/reports/templates", StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) &&
                IsInvoiceReportType(query?["reportType"].ToString()))
                return PermissionModuleCatalog.DocumentInvoiceReports;
            if (path.StartsWithSegments("/api/reports", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/api/tools/pdf", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/api/tools/letter-of-credit", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentReports;
            if (path.StartsWithSegments("/api/tools/excel", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentExcel;
            if (path.StartsWithSegments("/api/tools/ocr", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentOcr;
            if (path.StartsWithSegments("/api/tools/container-packing", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.DocumentContainerPacking;
            if (path.StartsWithSegments("/api/tools/exchange-rates", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.CommonExchangeRates;
            if (path.StartsWithSegments("/api/tools/email", StringComparison.OrdinalIgnoreCase))
                return PermissionModuleCatalog.CommonEmail;
            return null;
        }

        private static bool IsPaymentReportType(string reportType) =>
            string.Equals(reportType, "PaymentVoucher", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reportType, "PaymentDocument", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reportType, "Internal", StringComparison.OrdinalIgnoreCase);

        private static bool IsInvoiceReportType(string reportType) =>
            string.Equals(reportType, "ExportDocument", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reportType, "Invoice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reportType, "Export", StringComparison.OrdinalIgnoreCase);

        public static string GetRequiredAccessLevel(string method) =>
            HttpMethods.IsDelete(method)
                ? PermissionAccessLevel.Manage
                : HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
                    ? PermissionAccessLevel.View
                    : PermissionAccessLevel.Operate;
    }

    public static class ApiAuthenticationApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseExportDocManagerDesktopAccess(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiDesktopAccessMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerApiAuthentication(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiAuthenticationMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerLicenseRequirement(
            this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<ApiLicenseRequirementMiddleware>();
        }

        public static IApplicationBuilder UseExportDocManagerWorkspaceAccess(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            return app.UseMiddleware<ApiWorkspaceAccessMiddleware>();
        }
    }
}
