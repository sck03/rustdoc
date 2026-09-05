using System.Collections.Frozen;

namespace ExportDocManager.Services.Security
{
    public static class PermissionDataScope
    {
        public const string Own = "own";
        public const string Department = "department";
        public const string Company = "company";
        public const string All = "all";

        public static readonly IReadOnlyList<string> Values = [Own, Department, Company, All];

        public static bool IsKnown(string? value) =>
            Values.Contains(value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string? value) =>
            Values.FirstOrDefault(item => string.Equals(item, value?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;

        public static int Rank(string? value) => Normalize(value) switch
        {
            All => 4,
            Company => 3,
            Department => 2,
            Own => 1,
            _ => 0
        };

        public static string Min(string left, string right) =>
            Rank(left) <= Rank(right) ? Normalize(left) : Normalize(right);

        public static string Max(string left, string right) =>
            Rank(left) >= Rank(right) ? Normalize(left) : Normalize(right);
    }

    public static class PermissionAction
    {
        public const string View = "view";
        public const string Create = "create";
        public const string Edit = "edit";
        public const string Operate = "operate";
        public const string Manage = "manage";
        public const string Deactivate = "deactivate";
        public const string Delete = "delete";
        public const string Import = "import";
        public const string Export = "export";
        public const string Assign = "assign";
        public const string SetPrimary = "set-primary";
        public const string Complete = "complete";
        public const string Restore = "restore";
        public const string Transition = "transition";
        public const string Approve = "approve";
        public const string Archive = "archive";
        public const string Admit = "admit";
        public const string Publish = "publish";
        public const string Share = "share";
        public const string Clone = "clone";
        public const string Design = "design";
        public const string Upload = "upload";
        public const string Recycle = "recycle";
        public const string Preview = "preview";
        public const string Print = "print";
        public const string ExportPdf = "export-pdf";
        public const string ExportZip = "export-zip";
        public const string SendEmail = "send-email";
        public const string Send = "send";
        public const string ViewDelivery = "view-delivery";
        public const string Configure = "configure";
    }

    public sealed record PermissionActionDefinition(
        string Key,
        string Name,
        string Description,
        int SortOrder,
        string NavigationAccessLevel);

    public sealed record PermissionResourceDefinition(
        string Key,
        string Name,
        string Group,
        string Workspace,
        string ModuleKey,
        int SortOrder,
        bool IsTechnical,
        bool SupportsDataScope,
        IReadOnlyList<PermissionActionDefinition> Actions);

    public sealed record EffectivePermissionGrant(
        string ResourceKey,
        string Action,
        string DataScope,
        string Source,
        string SourceResourceKey = "");

    public static class PermissionResourceCatalog
    {
        public const string SalesDashboard = "sales.dashboard";
        public const string CrmCustomers = "sales.customers";
        public const string CrmContacts = "sales.contacts";
        public const string CrmFollowUps = "sales.follow-ups";
        public const string SalesOpportunities = "sales.opportunities";
        public const string SalesQuotes = "sales.quotes";
        public const string Suppliers = "sales.suppliers";
        public const string SupplierContacts = "sales.supplier-contacts";
        public const string SupplierProductLinks = "sales.supplier-product-links";
        public const string SupplierAssessments = "sales.supplier-assessments";
        public const string EmailTemplates = "sales.email-templates";
        public const string EmailDelivery = "common.email-delivery";
        public const string EmailPolicy = "system.email-policy";
        public const string ReportTemplates = "document.report-templates";
        public const string ReportResources = "document.report-resources";
        public const string InvoiceOutput = "document.invoice-output";
        public const string PaymentOutput = "document.payment-output";
        public const string SystemUsers = "system.users";
        public const string SystemPermissions = "system.permissions";
        public const string SystemSettings = "system.settings";
        public const string SystemAudit = "system.audit";
        public const string SystemBackup = "system.backup";
        public const string SystemDisasterRecovery = "system.disaster-recovery";

        private static PermissionActionDefinition Action(
            string key,
            string name,
            string description,
            int sortOrder,
            string navigationAccessLevel = PermissionAccessLevel.Operate) =>
            new(key, name, description, sortOrder, navigationAccessLevel);

        private static readonly PermissionActionDefinition ViewAction =
            Action(PermissionAction.View, "查看", "浏览、搜索和查看详情", 10, PermissionAccessLevel.View);

        private static IReadOnlyList<PermissionActionDefinition> StandardActions() =>
        [
            ViewAction,
            Action(PermissionAction.Operate, "操作", "执行日常业务操作", 20),
            Action(PermissionAction.Manage, "管理", "执行高风险管理操作", 30, PermissionAccessLevel.Manage)
        ];

        private static PermissionResourceDefinition StandardModule(PermissionModuleDefinition module) =>
            new(module.Key, module.Name, module.Group, module.Workspace, module.Key, module.SortOrder,
                module.IsTechnical, true, StandardActions());

        private static readonly HashSet<string> ReplacedModuleKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            PermissionModuleCatalog.SalesDashboard,
            PermissionModuleCatalog.SalesCrm,
            PermissionModuleCatalog.SalesOpportunities,
            PermissionModuleCatalog.SalesEmailTemplates,
            PermissionModuleCatalog.SalesSuppliers,
            PermissionModuleCatalog.DocumentReports,
            PermissionModuleCatalog.DocumentInvoiceReports,
            PermissionModuleCatalog.DocumentPaymentReports,
            PermissionModuleCatalog.CommonEmail,
            PermissionModuleCatalog.SystemDisasterRecovery
        };

        private static readonly IReadOnlyList<PermissionResourceDefinition> ResourceDefinitions =
        [
            .. PermissionModuleCatalog.Modules
                .Where(module => !ReplacedModuleKeys.Contains(module.Key))
                .Select(StandardModule),
            new(SalesDashboard, "销售概览", "销售业务", "sales", PermissionModuleCatalog.SalesDashboard, 200,
                false, true, [ViewAction]),
            new(CrmCustomers, "客户", "客户与跟进", "sales", PermissionModuleCatalog.SalesCrm, 210, false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建客户档案", 20),
                Action(PermissionAction.Edit, "编辑", "编辑客户档案", 30),
                Action(PermissionAction.Deactivate, "停用", "停用或恢复客户", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除无业务引用的草稿客户", 50, PermissionAccessLevel.Manage),
                Action(PermissionAction.Import, "导入", "批量导入客户", 60, PermissionAccessLevel.Manage),
                Action(PermissionAction.Export, "导出", "导出客户数据", 70, PermissionAccessLevel.Manage)
            ]),
            new(CrmContacts, "客户联系人", "客户与跟进", "sales", PermissionModuleCatalog.SalesCrm, 220, false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建联系人", 20),
                Action(PermissionAction.Edit, "编辑", "编辑联系人", 30),
                Action(PermissionAction.SetPrimary, "设为主要联系人", "原子切换主要联系人", 40),
                Action(PermissionAction.Delete, "删除", "删除联系人并保留业务历史", 50, PermissionAccessLevel.Manage)
            ]),
            new(CrmFollowUps, "跟进", "客户与跟进", "sales", PermissionModuleCatalog.SalesCrm, 230, false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建跟进记录", 20),
                Action(PermissionAction.Edit, "编辑", "编辑跟进内容", 30),
                Action(PermissionAction.Complete, "完成", "标记跟进完成", 40),
                Action(PermissionAction.Restore, "恢复", "恢复已完成跟进", 50, PermissionAccessLevel.Manage),
                Action(PermissionAction.Assign, "转移", "通过受审计命令转移跟进", 60, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除跟进记录", 70, PermissionAccessLevel.Manage)
            ]),
            new(SalesOpportunities, "商机", "商机与报价", "sales", PermissionModuleCatalog.SalesOpportunities, 240,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建商机", 20),
                Action(PermissionAction.Edit, "编辑", "编辑商机资料", 30),
                Action(PermissionAction.Transition, "阶段流转", "按状态机推进商机", 40),
                Action(PermissionAction.Archive, "归档", "归档商机并保留历史", 50, PermissionAccessLevel.Manage)
            ]),
            new(SalesQuotes, "报价版本", "商机与报价", "sales", PermissionModuleCatalog.SalesOpportunities, 250,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建版本", "创建报价版本", 20),
                Action(PermissionAction.Edit, "编辑", "编辑当前报价", 30)
            ]),
            new(Suppliers, "供应商", "供应商", "sales", PermissionModuleCatalog.SalesSuppliers, 260, false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建供应商", 20),
                Action(PermissionAction.Edit, "编辑", "编辑供应商档案", 30),
                Action(PermissionAction.Admit, "准入", "确认供应商准入", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.Deactivate, "停用", "停用或恢复供应商", 50, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除无业务引用的草稿供应商", 60, PermissionAccessLevel.Manage),
                Action(PermissionAction.Import, "导入", "批量导入供应商", 70, PermissionAccessLevel.Manage),
                Action(PermissionAction.Export, "导出", "导出供应商数据", 80, PermissionAccessLevel.Manage)
            ]),
            new(SupplierContacts, "供应商联系人", "供应商", "sales", PermissionModuleCatalog.SalesSuppliers, 270,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "新建", "新建供应商联系人", 20),
                Action(PermissionAction.Edit, "编辑", "编辑供应商联系人", 30),
                Action(PermissionAction.SetPrimary, "设为主要联系人", "原子切换主要联系人", 40),
                Action(PermissionAction.Delete, "删除", "删除供应商联系人", 50, PermissionAccessLevel.Manage)
            ]),
            new(SupplierProductLinks, "供货关系", "供应商", "sales", PermissionModuleCatalog.SalesSuppliers, 280,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Edit, "维护", "新建或编辑供货关系", 20),
                Action(PermissionAction.Deactivate, "停用", "停用供货关系", 30, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除供货关系", 40, PermissionAccessLevel.Manage)
            ]),
            new(SupplierAssessments, "供应商评价", "供应商", "sales", PermissionModuleCatalog.SalesSuppliers, 290,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Create, "填写", "填写供应商评价", 20),
                Action(PermissionAction.Edit, "修改", "修改供应商评价", 30),
                Action(PermissionAction.Approve, "确认", "确认供应商评价", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除供应商评价", 50, PermissionAccessLevel.Manage)
            ]),
            new(EmailTemplates, "邮件模板", "邮件与外发", "sales", PermissionModuleCatalog.SalesEmailTemplates, 300,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Edit, "编辑", "新建和编辑本人模板", 20),
                Action(PermissionAction.Publish, "发布", "发布模板", 30, PermissionAccessLevel.Manage),
                Action(PermissionAction.Share, "共享", "设置部门、公司或全局共享", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.Deactivate, "停用", "停用模板", 50, PermissionAccessLevel.Manage),
                Action(PermissionAction.Restore, "恢复", "恢复模板历史版本", 60, PermissionAccessLevel.Manage),
                Action(PermissionAction.Delete, "删除", "删除模板", 70, PermissionAccessLevel.Manage)
            ]),
            new(EmailDelivery, "邮件外发", "邮件与外发", "common", PermissionModuleCatalog.CommonEmail, 310,
                false, true,
            [
                Action(PermissionAction.Send, "单封发送", "发送单封业务邮件", 10),
                Action(PermissionAction.ViewDelivery, "投递记录", "查看邮件投递结果", 20, PermissionAccessLevel.View)
            ]),
            new(EmailPolicy, "邮件策略", "系统能力", "common", PermissionModuleCatalog.CommonEmail, 320, true,
                false, [Action(PermissionAction.Configure, "配置", "配置 SMTP 与外发策略", 10, PermissionAccessLevel.Manage)]),
            new(ReportTemplates, "报表模板", "报表设计", "document", PermissionModuleCatalog.DocumentReports, 330,
                false, true,
            [
                ViewAction,
                Action(PermissionAction.Design, "设计", "编辑本人模板草稿", 20),
                Action(PermissionAction.Clone, "复制", "复制模板为本人草稿", 30),
                Action(PermissionAction.Publish, "发布", "发布模板", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.Share, "共享", "设置模板共享范围", 50, PermissionAccessLevel.Manage),
                Action(PermissionAction.Deactivate, "停用", "停用模板", 60, PermissionAccessLevel.Manage),
                Action(PermissionAction.Archive, "归档", "归档模板", 70, PermissionAccessLevel.Manage),
                Action(PermissionAction.Restore, "恢复", "恢复模板或历史版本", 80, PermissionAccessLevel.Manage),
                Action(PermissionAction.Import, "导入", "导入模板包", 90, PermissionAccessLevel.Manage),
                Action(PermissionAction.Export, "导出", "导出模板包", 100, PermissionAccessLevel.Manage)
            ]),
            new(ReportResources, "模板图片资源", "报表设计", "document", PermissionModuleCatalog.DocumentReports,
                340, false, true,
            [
                ViewAction,
                Action(PermissionAction.Upload, "上传", "上传模板图片资源", 20),
                Action(PermissionAction.Recycle, "回收", "回收孤立资源", 30, PermissionAccessLevel.Manage)
            ]),
            OutputResource(InvoiceOutput, "发票单据输出", PermissionModuleCatalog.DocumentInvoiceReports, 350),
            new(PaymentOutput, "付款报销单据输出", "单据输出", "document",
                PermissionModuleCatalog.DocumentPaymentReports, 360, false, true,
            [
                Action(PermissionAction.Preview, "预览", "预览付款报销单据", 10, PermissionAccessLevel.View),
                Action(PermissionAction.Print, "打印", "打印付款报销单据", 20),
                Action(PermissionAction.ExportPdf, "PDF", "导出付款报销 PDF", 30)
            ]),
            SystemResource(SystemUsers, "用户管理", 900),
            SystemResource(SystemPermissions, "权限管理", 910),
            SystemResource(SystemSettings, "系统配置", 920),
            new(SystemAudit, "审计日志", "系统能力", "common", PermissionModuleCatalog.SystemAbout, 930, true,
                false,
            [
                Action(PermissionAction.View, "查看", "查看安全审计", 10, PermissionAccessLevel.View),
                Action(PermissionAction.Manage, "维护", "导出与清理审计日志", 20, PermissionAccessLevel.Manage)
            ]),
            SystemResource(SystemBackup, "备份维护", 940),
            SystemResource(SystemDisasterRecovery, "灾备与迁移", 950)
        ];

        public static readonly IReadOnlyList<PermissionResourceDefinition> Resources = ResourceDefinitions
            .OrderBy(resource => resource.SortOrder)
            .ThenBy(resource => resource.Key, StringComparer.Ordinal)
            .ToArray();

        public static readonly IReadOnlyDictionary<string, PermissionResourceDefinition> ByKey =
            Resources.ToFrozenDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnownResource(string? resourceKey) =>
            !string.IsNullOrWhiteSpace(resourceKey) && ByKey.ContainsKey(resourceKey.Trim());

        public static bool IsKnownAction(string? resourceKey, string? action) =>
            IsKnownResource(resourceKey) &&
            ByKey[resourceKey!.Trim()].Actions.Any(item =>
                string.Equals(item.Key, action?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string CreateGrantKey(string resourceKey, string action) =>
            $"{resourceKey.Trim().ToLowerInvariant()}\u001f{action.Trim().ToLowerInvariant()}";

        public static IReadOnlyList<EffectivePermissionGrant> ExpandDependencies(
            IEnumerable<PermissionGrantRecord> grants)
        {
            var direct = grants.Select(NormalizeGrant).ToArray();
            var effective = direct.ToDictionary(
                grant => CreateGrantKey(grant.ResourceKey, grant.Action),
                grant => new EffectivePermissionGrant(
                    grant.ResourceKey, grant.Action, grant.DataScope, "template"),
                StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<PermissionGrantRecord>(direct);
            var expandedScopeRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            while (pending.TryDequeue(out var grant))
            {
                string grantKey = CreateGrantKey(grant.ResourceKey, grant.Action);
                int grantScopeRank = PermissionDataScope.Rank(grant.DataScope);
                if (expandedScopeRanks.TryGetValue(grantKey, out int expandedScopeRank) &&
                    expandedScopeRank >= grantScopeRank)
                {
                    continue;
                }

                expandedScopeRanks[grantKey] = grantScopeRank;
                foreach (var dependency in ResolveDependencies(grant))
                {
                    string key = CreateGrantKey(dependency.ResourceKey, dependency.Action);
                    if (effective.TryGetValue(key, out var existing))
                    {
                        string mergedScope = PermissionDataScope.Max(existing.DataScope, dependency.DataScope);
                        if (PermissionDataScope.Rank(mergedScope) > PermissionDataScope.Rank(existing.DataScope))
                        {
                            effective[key] = existing with { DataScope = mergedScope };
                            pending.Enqueue(new PermissionGrantRecord(
                                dependency.ResourceKey,
                                dependency.Action,
                                mergedScope));
                        }
                    }
                    else
                    {
                        effective[key] = dependency;
                        pending.Enqueue(new PermissionGrantRecord(
                            dependency.ResourceKey,
                            dependency.Action,
                            dependency.DataScope));
                    }
                }
            }

            return effective.Values
                .OrderBy(item => ByKey[item.ResourceKey].SortOrder)
                .ThenBy(item => ByKey[item.ResourceKey].Actions.Single(action =>
                    string.Equals(action.Key, item.Action, StringComparison.OrdinalIgnoreCase)).SortOrder)
                .ToArray();
        }

        public static PermissionGrantRecord NormalizeGrant(PermissionGrantRecord grant)
        {
            string resourceKey = grant.ResourceKey?.Trim() ?? string.Empty;
            string action = grant.Action?.Trim() ?? string.Empty;
            if (!IsKnownAction(resourceKey, action))
            {
                throw new ArgumentException($"未知权限动作：{resourceKey}/{action}。", nameof(grant));
            }

            string dataScope = PermissionDataScope.Normalize(grant.DataScope);
            if (string.IsNullOrEmpty(dataScope))
            {
                throw new ArgumentException($"权限 {resourceKey}/{action} 的数据范围无效。", nameof(grant));
            }

            var resource = ByKey[resourceKey];
            return new PermissionGrantRecord(
                resource.Key,
                resource.Actions.Single(item => string.Equals(item.Key, action, StringComparison.OrdinalIgnoreCase)).Key,
                resource.SupportsDataScope ? dataScope : PermissionDataScope.All);
        }

        public static string GetNavigationAccessLevel(string resourceKey, string action)
        {
            if (!IsKnownAction(resourceKey, action)) return PermissionAccessLevel.None;
            return ByKey[resourceKey].Actions.Single(item =>
                string.Equals(item.Key, action, StringComparison.OrdinalIgnoreCase)).NavigationAccessLevel;
        }

        public static IReadOnlyList<PermissionGrantRecord> CreatePreset(
            string resourceKey,
            string accessLevel,
            string dataScope)
        {
            if (!IsKnownResource(resourceKey) || !PermissionAccessLevel.IsKnown(accessLevel))
            {
                throw new ArgumentException("权限资源或快捷预设无效。");
            }

            var resource = ByKey[resourceKey];
            return resource.Actions
                .Where(action => PermissionAccessLevel.Rank(action.NavigationAccessLevel) <=
                    PermissionAccessLevel.Rank(accessLevel))
                .Select(action => NormalizeGrant(new PermissionGrantRecord(resource.Key, action.Key, dataScope)))
                .ToArray();
        }

        private static IEnumerable<EffectivePermissionGrant> ResolveDependencies(PermissionGrantRecord grant)
        {
            if (grant.ResourceKey == PermissionModuleCatalog.DocumentInvoices)
            {
                yield return Inherited(PermissionModuleCatalog.DocumentReferenceData, PermissionAction.View,
                    grant, PermissionDataScope.All);
                yield return Inherited(PermissionModuleCatalog.CommonProductReference, PermissionAction.View,
                    grant, PermissionDataScope.All);
                yield return Inherited(PermissionModuleCatalog.DocumentCustomOptions, PermissionAction.Operate,
                    grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey == PermissionModuleCatalog.DocumentPayments)
            {
                yield return Inherited(PermissionModuleCatalog.DocumentReferenceData, PermissionAction.View,
                    grant, PermissionDataScope.All);
                yield return Inherited(PermissionModuleCatalog.DocumentCustomOptions, PermissionAction.Operate,
                    grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey == PermissionModuleCatalog.DocumentQuery)
            {
                yield return Inherited(PermissionModuleCatalog.DocumentReferenceData, PermissionAction.View,
                    grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey == PermissionModuleCatalog.DocumentMasterData)
            {
                yield return Inherited(PermissionModuleCatalog.DocumentReferenceData, PermissionAction.View,
                    grant, PermissionDataScope.All);
                yield return Inherited(PermissionModuleCatalog.CommonProductReference, PermissionAction.View,
                    grant, PermissionDataScope.All);
                yield return Inherited(PermissionModuleCatalog.DocumentCustomOptions, PermissionAction.Operate,
                    grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey is CrmContacts or CrmFollowUps or SalesOpportunities or SalesQuotes)
            {
                yield return Inherited(CrmCustomers, PermissionAction.View, grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey is SalesOpportunities or SalesQuotes)
            {
                yield return Inherited(
                    PermissionModuleCatalog.CommonProductReference,
                    PermissionAction.View,
                    grant,
                    PermissionDataScope.All);
            }

            if (grant.ResourceKey is Suppliers or SupplierProductLinks)
            {
                yield return Inherited(
                    PermissionModuleCatalog.CommonProductReference,
                    PermissionAction.View,
                    grant,
                    PermissionDataScope.All);
            }

            if (grant.ResourceKey is ReportTemplates or ReportResources)
            {
                yield return Inherited(ReportResources, PermissionAction.View, grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey is InvoiceOutput or PaymentOutput)
            {
                yield return Inherited(
                    grant.ResourceKey == InvoiceOutput
                        ? PermissionModuleCatalog.DocumentInvoices
                        : PermissionModuleCatalog.DocumentPayments,
                    PermissionAction.View,
                    grant,
                    PermissionDataScope.All);
                yield return Inherited(
                    ReportTemplates,
                    PermissionAction.View,
                    grant,
                    PermissionDataScope.All);

                if (grant.Action == PermissionAction.Print)
                {
                    yield return Inherited(
                        grant.ResourceKey,
                        PermissionAction.Preview,
                        grant,
                        PermissionDataScope.All);
                }
            }

            if (grant.ResourceKey is InvoiceOutput or PaymentOutput && grant.Action == PermissionAction.SendEmail)
            {
                yield return Inherited(EmailDelivery, PermissionAction.Send, grant, PermissionDataScope.All);
            }

            if (grant.ResourceKey == EmailDelivery && grant.Action == PermissionAction.Send)
            {
                yield return Inherited(EmailDelivery, PermissionAction.ViewDelivery, grant, PermissionDataScope.All);
            }
        }

        private static EffectivePermissionGrant Inherited(
            string resourceKey,
            string action,
            PermissionGrantRecord source,
            string maximumScope) =>
            new(resourceKey, action, PermissionDataScope.Min(source.DataScope, maximumScope),
                "dependency", source.ResourceKey);

        private static PermissionResourceDefinition OutputResource(
            string key,
            string name,
            string moduleKey,
            int sortOrder) =>
            new(key, name, "单据输出", "document", moduleKey, sortOrder, false, true,
            [
                Action(PermissionAction.Preview, "预览", "预览单据", 10, PermissionAccessLevel.View),
                Action(PermissionAction.Print, "打印", "打印单据", 20),
                Action(PermissionAction.ExportPdf, "PDF", "导出 PDF", 30),
                Action(PermissionAction.ExportZip, "ZIP", "批量导出 ZIP", 40, PermissionAccessLevel.Manage),
                Action(PermissionAction.SendEmail, "邮件外发", "通过邮件外发单据", 50, PermissionAccessLevel.Manage)
            ]);

        private static PermissionResourceDefinition SystemResource(string key, string name, int sortOrder) =>
            new(key, name, "系统能力", "common", PermissionModuleCatalog.SystemAbout, sortOrder, true, false,
                [Action(PermissionAction.Manage, "管理", $"管理{name}", 10, PermissionAccessLevel.Manage)]);
    }
}
