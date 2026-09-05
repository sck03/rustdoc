using System.Collections.Frozen;

namespace ExportDocManager.Services.Security
{
    public static class PermissionAccessLevel
    {
        // `none` is an internal, fail-closed value used when a persisted
        // record is corrupt or a client sends an unknown value.  It is not an
        // assignable option and therefore remains excluded from Levels.
        public const string None = "none";
        public const string View = "view";
        public const string Operate = "operate";
        public const string Manage = "manage";

        public static readonly IReadOnlyList<string> Levels = [View, Operate, Manage];

        public static bool IsKnown(string value) =>
            Levels.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string value) =>
            Levels.FirstOrDefault(level => string.Equals(level, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? None;

        public static int Rank(string value) => Normalize(value) switch
        {
            Manage => 3,
            Operate => 2,
            View => 1,
            _ => 0
        };

        public static string Min(string left, string right) =>
            Rank(left) <= Rank(right) ? Normalize(left) : Normalize(right);

        public static string Max(string left, string right) =>
            Rank(left) >= Rank(right) ? Normalize(left) : Normalize(right);
    }

    public sealed record PermissionModuleDefinition(
        string Key,
        string Name,
        string Group,
        string Workspace,
        int SortOrder,
        bool IsTechnical = false);

    public sealed record BuiltInPermissionTemplateDefinition(
        string Code,
        string Name,
        string Description,
        IReadOnlyList<PermissionGrantRecord> Grants)
    {
        public IReadOnlyList<EffectivePermissionGrant> GetEffectivePermissions() =>
            PermissionResourceCatalog.ExpandDependencies(Grants);
    }

    public static class PermissionModuleCatalog
    {
        public const string DocumentDashboard = "document.dashboard";
        public const string DocumentInvoices = "document.invoices";
        public const string DocumentQuery = "document.query";
        public const string DocumentPayments = "document.payments";
        public const string DocumentJobs = "document.jobs";
        public const string DocumentSingleWindow = "document.single-window";
        public const string DocumentDeclarationDictionary = "document.declaration-dictionary";
        public const string DocumentHsKnowledge = "document.hs-knowledge";
        public const string DocumentMasterData = "document.master-data";
        public const string DocumentReports = "document.reports";
        public const string DocumentInvoiceReports = "document.invoice-reports";
        public const string DocumentPaymentReports = "document.payment-reports";
        public const string DocumentExcel = "document.excel";
        public const string DocumentOcr = "document.ocr";
        public const string DocumentContainerPacking = "document.container-packing";
        public const string DocumentCustomOptions = "document.custom-options";
        public const string DocumentReferenceData = "document.reference-data";
        public const string SalesDashboard = "sales.dashboard";
        public const string SalesCrm = "sales.crm";
        public const string SalesOpportunities = "sales.opportunities";
        public const string SalesEmailTemplates = "sales.email-templates";
        public const string SalesSuppliers = "sales.suppliers";
        public const string CommonProductReference = "common.product-reference";
        public const string CommonExchangeRates = "common.exchange-rates";
        public const string CommonEmail = "common.email";
        public const string SystemDisasterRecovery = "system.disaster-recovery";
        public const string SystemAbout = "system.about";

        public static readonly IReadOnlyList<PermissionModuleDefinition> Modules =
        [
            new(DocumentDashboard, "单证仪表盘", "单证业务", "document", 10),
            new(DocumentInvoices, "发票管理", "单证业务", "document", 20),
            new(DocumentQuery, "单据查询", "单证业务", "document", 30),
            new(DocumentPayments, "付款报销", "单证业务", "document", 40),
            new(DocumentJobs, "任务中心", "单证业务", "document", 50),
            new(DocumentSingleWindow, "单一窗口", "单证业务", "document", 60),
            new(DocumentDeclarationDictionary, "申报词典", "申报与归类", "document", 62),
            new(DocumentHsKnowledge, "HS 编码知识", "申报与归类", "document", 65),
            new(DocumentMasterData, "主数据维护", "单证业务", "document", 70),
            new(DocumentReports, "报表设计", "单证工具", "document", 80),
            new(DocumentInvoiceReports, "发票单据输出", "单证基础能力", "document", 84, true),
            new(DocumentPaymentReports, "付款报销单据输出", "单证基础能力", "document", 85, true),
            new(DocumentExcel, "Excel 工具", "单证工具", "document", 90),
            new(DocumentOcr, "智能 OCR", "通用工具", "document", 100),
            new(DocumentContainerPacking, "装箱模拟", "单证工具", "document", 110),
            new(DocumentCustomOptions, "单证候选项", "单证基础能力", "document", 120, true),
            new(DocumentReferenceData, "业务基础资料读取", "单证基础能力", "document", 130, true),
            new(SalesDashboard, "销售概览", "销售业务", "sales", 200),
            new(SalesCrm, "客户与跟进", "销售业务", "sales", 210),
            new(SalesOpportunities, "商机跟踪", "销售业务", "sales", 220),
            new(SalesEmailTemplates, "邮件模板", "销售业务", "sales", 230),
            new(SalesSuppliers, "供应商管理", "销售业务", "sales", 240),
            new(CommonProductReference, "商品资料读取", "通用基础能力", "common", 290, true),
            new(CommonExchangeRates, "今日汇率", "通用工具", "common", 300),
            new(CommonEmail, "邮件发送", "通用工具", "common", 310),
            new(SystemDisasterRecovery, "灾难恢复", "系统", "common", 315, true),
            new(SystemAbout, "关于系统", "系统", "common", 320)
        ];

        public static readonly IReadOnlyDictionary<string, PermissionModuleDefinition> ByKey =
            Modules.ToFrozenDictionary(module => module.Key, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string moduleKey) =>
            !string.IsNullOrWhiteSpace(moduleKey) && ByKey.ContainsKey(moduleKey.Trim());

    }

    public static class BuiltInPermissionTemplateCatalog
    {
        public const string Admin = "Admin";
        public const string Document = "User";
        public const string Sales = "Sales";
        public const string SalesManager = "SalesManager";
        public const string Finance = "Finance";

        public static readonly IReadOnlyList<BuiltInPermissionTemplateDefinition> Templates =
        [
            new(Admin, "系统管理员", "全部已实现业务模块；系统维护能力仍由管理员身份保护。",
                PermissionResourceCatalog.Resources
                    .SelectMany(resource => resource.Actions.Select(action =>
                        new PermissionGrantRecord(resource.Key, action.Key, PermissionDataScope.All)))
                    .ToArray()),
            new(Document, "单证人员", "单证业务、本人业务数据、本人报表模板和通用工具。",
                BuildDocumentGrants()),
            new(Sales, "业务人员", "维护本人客户、跟进和商机；供应商及共享主数据默认只读。",
                BuildSalesGrants(PermissionDataScope.Own, supervisor: false)),
            new(SalesManager, "销售主管", "管理部门客户、跟进、商机、供应商和共享模板；系统能力仍禁止。",
                BuildSalesGrants(PermissionDataScope.Department, supervisor: true)),
            new(Finance, "财务人员", "维护本人付款报销，使用报表、汇率、邮件和 OCR。",
                BuildFinanceGrants())
        ];

        private static IReadOnlyList<PermissionGrantRecord> BuildDocumentGrants() =>
        [
            .. Preset(PermissionModuleCatalog.DocumentDashboard, PermissionAccessLevel.View),
            .. Preset(PermissionModuleCatalog.DocumentInvoices, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentQuery, PermissionAccessLevel.View),
            .. Preset(PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentJobs, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentSingleWindow, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentDeclarationDictionary, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentHsKnowledge, PermissionAccessLevel.View),
            .. Preset(PermissionModuleCatalog.DocumentMasterData, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentExcel, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentOcr, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentContainerPacking, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.CommonExchangeRates, PermissionAccessLevel.View),
            .. Preset(PermissionModuleCatalog.SystemAbout, PermissionAccessLevel.View),
            new(PermissionResourceCatalog.ReportTemplates, PermissionAction.View, PermissionDataScope.Department),
            .. Grant(PermissionResourceCatalog.ReportTemplates, PermissionDataScope.Own,
                PermissionAction.Design, PermissionAction.Clone),
            new(PermissionResourceCatalog.ReportResources, PermissionAction.View, PermissionDataScope.Department),
            new(PermissionResourceCatalog.ReportResources, PermissionAction.Upload, PermissionDataScope.Own),
            new(PermissionResourceCatalog.EmailTemplates, PermissionAction.View, PermissionDataScope.Department),
            .. Grant(PermissionResourceCatalog.InvoiceOutput, PermissionDataScope.Own,
                PermissionAction.Preview, PermissionAction.Print, PermissionAction.ExportPdf, PermissionAction.ExportZip,
                PermissionAction.SendEmail),
            .. Grant(PermissionResourceCatalog.PaymentOutput, PermissionDataScope.Own,
                PermissionAction.Preview, PermissionAction.Print, PermissionAction.ExportPdf),
            .. Grant(PermissionResourceCatalog.EmailDelivery, PermissionDataScope.Own,
                PermissionAction.Send, PermissionAction.ViewDelivery)
        ];

        private static IReadOnlyList<PermissionGrantRecord> BuildSalesGrants(string scope, bool supervisor)
        {
            var grants = new List<PermissionGrantRecord>(
            [
                new(PermissionResourceCatalog.SalesDashboard, PermissionAction.View, scope),
                .. Grant(PermissionResourceCatalog.CrmCustomers, scope,
                    PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Deactivate),
                .. Grant(PermissionResourceCatalog.CrmContacts, scope,
                    PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.SetPrimary),
                .. Grant(PermissionResourceCatalog.CrmFollowUps, scope,
                    PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Complete),
                .. Grant(PermissionResourceCatalog.SalesOpportunities, scope,
                    PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Transition),
                .. Grant(PermissionResourceCatalog.SalesQuotes, scope,
                    PermissionAction.View, PermissionAction.Create, PermissionAction.Edit),
                new(PermissionResourceCatalog.Suppliers, PermissionAction.View,
                    supervisor ? scope : PermissionDataScope.Company),
                new(PermissionResourceCatalog.SupplierContacts, PermissionAction.View,
                    supervisor ? scope : PermissionDataScope.Company),
                new(PermissionResourceCatalog.SupplierProductLinks, PermissionAction.View,
                    supervisor ? scope : PermissionDataScope.Company),
                new(PermissionResourceCatalog.SupplierAssessments, PermissionAction.View,
                    supervisor ? scope : PermissionDataScope.Company),
                new(PermissionResourceCatalog.EmailTemplates, PermissionAction.View,
                    PermissionDataScope.Department),
                .. Grant(PermissionResourceCatalog.EmailDelivery, scope,
                    PermissionAction.Send, PermissionAction.ViewDelivery),
                .. Preset(PermissionModuleCatalog.CommonProductReference, PermissionAccessLevel.View,
                    PermissionDataScope.All),
                .. Preset(PermissionModuleCatalog.CommonExchangeRates, PermissionAccessLevel.View,
                    PermissionDataScope.All),
                .. Preset(PermissionModuleCatalog.SystemAbout, PermissionAccessLevel.View,
                    PermissionDataScope.All)
            ]);

            if (supervisor)
            {
                grants.AddRange(Grant(PermissionResourceCatalog.CrmCustomers, scope,
                    PermissionAction.Export));
                grants.AddRange(Grant(PermissionResourceCatalog.CrmFollowUps, scope,
                    PermissionAction.Restore, PermissionAction.Assign));
                grants.AddRange(Grant(PermissionResourceCatalog.SalesOpportunities, scope,
                    PermissionAction.Archive));
                grants.AddRange(Grant(PermissionResourceCatalog.Suppliers, scope,
                    PermissionAction.Create, PermissionAction.Edit, PermissionAction.Admit,
                    PermissionAction.Deactivate, PermissionAction.Import, PermissionAction.Export));
                grants.AddRange(Grant(PermissionResourceCatalog.SupplierContacts, scope,
                    PermissionAction.Create, PermissionAction.Edit, PermissionAction.SetPrimary));
                grants.AddRange(Grant(PermissionResourceCatalog.SupplierProductLinks, scope,
                    PermissionAction.Edit, PermissionAction.Deactivate));
                grants.AddRange(Grant(PermissionResourceCatalog.SupplierAssessments, scope,
                    PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve));
                grants.AddRange(Grant(PermissionResourceCatalog.EmailTemplates, scope,
                    PermissionAction.Edit, PermissionAction.Publish, PermissionAction.Share, PermissionAction.Deactivate,
                    PermissionAction.Restore));
            }

            return grants;
        }

        private static IReadOnlyList<PermissionGrantRecord> BuildFinanceGrants() =>
        [
            .. Preset(PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.DocumentQuery, PermissionAccessLevel.View),
            .. Preset(PermissionModuleCatalog.DocumentOcr, PermissionAccessLevel.Operate),
            .. Preset(PermissionModuleCatalog.CommonExchangeRates, PermissionAccessLevel.View,
                PermissionDataScope.All),
            .. Preset(PermissionModuleCatalog.SystemAbout, PermissionAccessLevel.View,
                PermissionDataScope.All),
            new(PermissionResourceCatalog.ReportTemplates, PermissionAction.View, PermissionDataScope.Department),
            .. Grant(PermissionResourceCatalog.ReportTemplates, PermissionDataScope.Own,
                PermissionAction.Design, PermissionAction.Clone),
            new(PermissionResourceCatalog.ReportResources, PermissionAction.View, PermissionDataScope.Department),
            new(PermissionResourceCatalog.ReportResources, PermissionAction.Upload, PermissionDataScope.Own),
            new(PermissionResourceCatalog.EmailTemplates, PermissionAction.View, PermissionDataScope.Department),
            .. Grant(PermissionResourceCatalog.PaymentOutput, PermissionDataScope.Own,
                PermissionAction.Preview, PermissionAction.Print, PermissionAction.ExportPdf),
            .. Grant(PermissionResourceCatalog.EmailDelivery, PermissionDataScope.Own,
                PermissionAction.Send, PermissionAction.ViewDelivery)
        ];

        private static IReadOnlyList<PermissionGrantRecord> Preset(
            string resourceKey,
            string accessLevel,
            string scope = PermissionDataScope.Own) =>
            PermissionResourceCatalog.CreatePreset(resourceKey, accessLevel, scope);

        private static IReadOnlyList<PermissionGrantRecord> Grant(
            string resourceKey,
            string scope,
            params string[] actions) =>
            actions.Select(action => new PermissionGrantRecord(resourceKey, action, scope)).ToArray();

        public static BuiltInPermissionTemplateDefinition FindForRole(string role)
        {
            string normalizedRole = (role ?? string.Empty).Trim();
            return Templates.SingleOrDefault(template =>
                       string.Equals(template.Code, normalizedRole, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException("用户角色没有对应的内置权限模板。", nameof(role));
        }
    }
}
