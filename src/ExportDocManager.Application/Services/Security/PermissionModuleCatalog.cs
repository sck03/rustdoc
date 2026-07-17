namespace ExportDocManager.Services.Security
{
    public static class PermissionAccessLevel
    {
        public const string View = "view";
        public const string Operate = "operate";
        public const string Manage = "manage";

        public static readonly IReadOnlyList<string> Levels = [View, Operate, Manage];

        public static bool IsKnown(string value) =>
            Levels.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string value) =>
            Levels.FirstOrDefault(level => string.Equals(level, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? View;

        public static int Rank(string value) => Normalize(value) switch
        {
            Manage => 3,
            Operate => 2,
            _ => 1
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
        IReadOnlyList<string> ModuleKeys,
        IReadOnlyDictionary<string, string> AccessOverrides = null)
    {
        public string GetAccessLevel(string moduleKey) =>
            AccessOverrides != null && AccessOverrides.TryGetValue(moduleKey, out var accessLevel)
                ? PermissionAccessLevel.Normalize(accessLevel)
                : PermissionAccessLevel.Manage;

        public IReadOnlyDictionary<string, string> GetModuleAccess() =>
            PermissionModuleCatalog.ExpandDependencies(ModuleKeys.Select(moduleKey =>
                new PermissionTemplateModuleRecord(moduleKey, GetAccessLevel(moduleKey))));
    }

    public static class PermissionModuleCatalog
    {
        public const string DocumentDashboard = "document.dashboard";
        public const string DocumentInvoices = "document.invoices";
        public const string DocumentQuery = "document.query";
        public const string DocumentPayments = "document.payments";
        public const string DocumentJobs = "document.jobs";
        public const string DocumentSingleWindow = "document.single-window";
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
        public const string SystemAbout = "system.about";
        public const string FinanceDashboard = "finance.dashboard";

        public static readonly IReadOnlyList<PermissionModuleDefinition> Modules =
        [
            new(DocumentDashboard, "单证仪表盘", "单证业务", "document", 10),
            new(DocumentInvoices, "发票管理", "单证业务", "document", 20),
            new(DocumentQuery, "单据查询", "单证业务", "document", 30),
            new(DocumentPayments, "付款报销", "单证业务", "document", 40),
            new(DocumentJobs, "任务中心", "单证业务", "document", 50),
            new(DocumentSingleWindow, "单一窗口", "单证业务", "document", 60),
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
            new(SystemAbout, "关于系统", "系统", "common", 320)
        ];

        public static readonly IReadOnlyDictionary<string, PermissionModuleDefinition> ByKey =
            Modules.ToDictionary(module => module.Key, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string moduleKey) =>
            !string.IsNullOrWhiteSpace(moduleKey) && ByKey.ContainsKey(moduleKey.Trim());

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<PermissionModuleDependency>> Dependencies =
            new Dictionary<string, IReadOnlyList<PermissionModuleDependency>>(StringComparer.OrdinalIgnoreCase)
            {
                [DocumentInvoices] =
                [
                    new(DocumentReferenceData, PermissionAccessLevel.View),
                    new(CommonProductReference, PermissionAccessLevel.View),
                    new(DocumentCustomOptions, PermissionAccessLevel.Operate),
                    new(DocumentInvoiceReports, PermissionAccessLevel.Manage)
                ],
                [DocumentPayments] =
                [
                    new(DocumentReferenceData, PermissionAccessLevel.View),
                    new(DocumentCustomOptions, PermissionAccessLevel.Operate),
                    new(DocumentPaymentReports, PermissionAccessLevel.Manage)
                ],
                [DocumentQuery] = [new(DocumentReferenceData, PermissionAccessLevel.View)],
                [DocumentMasterData] =
                [
                    new(DocumentReferenceData, PermissionAccessLevel.View),
                    new(CommonProductReference, PermissionAccessLevel.View),
                    new(DocumentCustomOptions, PermissionAccessLevel.Operate)
                ],
                [SalesOpportunities] = [new(CommonProductReference, PermissionAccessLevel.View)],
                [SalesSuppliers] = [new(CommonProductReference, PermissionAccessLevel.View)]
            };

        public static IReadOnlyDictionary<string, string> ExpandDependencies(
            IEnumerable<PermissionTemplateModuleRecord> modules)
        {
            var expanded = (modules ?? [])
                .Where(module => IsKnown(module.ModuleKey) && PermissionAccessLevel.IsKnown(module.AccessLevel))
                .GroupBy(module => module.ModuleKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => PermissionAccessLevel.Normalize(group.Last().AccessLevel),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var source in expanded.ToArray())
            {
                if (!Dependencies.TryGetValue(source.Key, out var dependencies)) continue;
                foreach (var dependency in dependencies)
                {
                    string dependencyAccess = PermissionAccessLevel.Min(source.Value, dependency.MaximumAccessLevel);
                    expanded[dependency.ModuleKey] = expanded.TryGetValue(dependency.ModuleKey, out var currentAccess)
                        ? PermissionAccessLevel.Max(currentAccess, dependencyAccess)
                        : dependencyAccess;
                }
            }

            return expanded
                .OrderBy(item => ByKey[item.Key].SortOrder)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        }

        private sealed record PermissionModuleDependency(string ModuleKey, string MaximumAccessLevel);
    }

    public static class BuiltInPermissionTemplateCatalog
    {
        public const string Admin = "Admin";
        public const string Document = "User";
        public const string Sales = "Sales";
        public const string Finance = "Finance";

        private static readonly string[] CommonModules =
        [
            PermissionModuleCatalog.CommonExchangeRates,
            PermissionModuleCatalog.CommonEmail,
            PermissionModuleCatalog.SystemAbout
        ];

        private static readonly string[] DocumentModules = PermissionModuleCatalog.Modules
            .Where(module => module.Workspace == "document" && !module.IsTechnical)
            .Select(module => module.Key)
            .Concat(CommonModules)
            .ToArray();

        private static readonly string[] SalesModules = PermissionModuleCatalog.Modules
            .Where(module => module.Workspace == "sales")
            .Select(module => module.Key)
            .Concat(CommonModules)
            .ToArray();

        public static readonly IReadOnlyList<BuiltInPermissionTemplateDefinition> Templates =
        [
            new(Admin, "系统管理员", "全部已实现业务模块；系统维护能力仍由管理员身份保护。",
                PermissionModuleCatalog.Modules.Select(module => module.Key).ToArray()),
            new(Document, "单证人员", "完整单证工作区和通用工具。", DocumentModules,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Ordinary document users may create and maintain their own templates.
                    // Global file-template lifecycle and administrator maintenance still require Manage.
                    [PermissionModuleCatalog.DocumentReports] = PermissionAccessLevel.Operate
                }),
            new(Sales, "业务人员", "销售、供应商和通用工具。", SalesModules),
            new(Finance, "财务人员", "付款报销、单据查询、报表设计、汇率、邮件、OCR 和关于。",
                [
                    PermissionModuleCatalog.DocumentPayments,
                    PermissionModuleCatalog.DocumentQuery,
                    PermissionModuleCatalog.DocumentOcr,
                    PermissionModuleCatalog.DocumentReports,
                    PermissionModuleCatalog.CommonExchangeRates,
                    PermissionModuleCatalog.CommonEmail,
                    PermissionModuleCatalog.SystemAbout
                ])
        ];

        public static BuiltInPermissionTemplateDefinition FindForRole(string role) =>
            Templates.FirstOrDefault(template =>
                string.Equals(template.Code, role?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? Templates.First(template => template.Code == Document);
    }
}
