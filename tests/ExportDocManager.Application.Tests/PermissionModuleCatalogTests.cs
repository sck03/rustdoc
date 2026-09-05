using ExportDocManager.Services.Security;

namespace ExportDocManager.Application.Tests
{
    public class PermissionModuleCatalogTests
    {
        [Fact]
        public void ExpandDependencies_ShouldAddDocumentSupportWithoutGrantingOutput()
        {
            var result = PermissionResourceCatalog.ExpandDependencies(
            [
                new(PermissionModuleCatalog.DocumentPayments, PermissionAction.Operate,
                    PermissionDataScope.Department)
            ]);

            AssertGrant(result, PermissionModuleCatalog.DocumentPayments, PermissionAction.Operate,
                PermissionDataScope.Department, "template");
            AssertGrant(result, PermissionModuleCatalog.DocumentCustomOptions, PermissionAction.Operate,
                PermissionDataScope.Department, "dependency");
            AssertGrant(result, PermissionModuleCatalog.DocumentReferenceData, PermissionAction.View,
                PermissionDataScope.Department, "dependency");
            Assert.DoesNotContain(result, grant => grant.ResourceKey == PermissionResourceCatalog.PaymentOutput);
        }

        [Fact]
        public void ExpandDependencies_ShouldRequireEmailCapabilityForDocumentEmailOutput()
        {
            var result = PermissionResourceCatalog.ExpandDependencies(
            [
                new(PermissionResourceCatalog.InvoiceOutput, PermissionAction.SendEmail,
                    PermissionDataScope.Company)
            ]);

            AssertGrant(result, PermissionResourceCatalog.EmailDelivery, PermissionAction.Send,
                PermissionDataScope.Company, "dependency");
            AssertGrant(result, PermissionResourceCatalog.ReportTemplates, PermissionAction.View,
                PermissionDataScope.Company, "dependency");
            AssertGrant(result, PermissionModuleCatalog.DocumentInvoices, PermissionAction.View,
                PermissionDataScope.Company, "dependency");
            AssertGrant(result, PermissionResourceCatalog.EmailDelivery, PermissionAction.ViewDelivery,
                PermissionDataScope.Company, "dependency");
        }

        [Fact]
        public void ExpandDependencies_ShouldAllowSenderToReadDeliveryOutcomeWithinSameScope()
        {
            var result = PermissionResourceCatalog.ExpandDependencies(
            [
                new(PermissionResourceCatalog.EmailDelivery, PermissionAction.Send,
                    PermissionDataScope.Department)
            ]);

            AssertGrant(result, PermissionResourceCatalog.EmailDelivery, PermissionAction.Send,
                PermissionDataScope.Department, "template");
            AssertGrant(result, PermissionResourceCatalog.EmailDelivery, PermissionAction.ViewDelivery,
                PermissionDataScope.Department, "dependency");
        }

        [Fact]
        public void ExpandDependencies_ShouldMakePrintDependOnPreviewAndTemplateRead()
        {
            var result = PermissionResourceCatalog.ExpandDependencies(
            [
                new(PermissionResourceCatalog.PaymentOutput, PermissionAction.Print,
                    PermissionDataScope.Own)
            ]);

            AssertGrant(result, PermissionResourceCatalog.PaymentOutput, PermissionAction.Preview,
                PermissionDataScope.Own, "dependency");
            AssertGrant(result, PermissionResourceCatalog.ReportTemplates, PermissionAction.View,
                PermissionDataScope.Own, "dependency");
            AssertGrant(result, PermissionModuleCatalog.DocumentPayments, PermissionAction.View,
                PermissionDataScope.Own, "dependency");
        }

        [Fact]
        public void ExpandDependencies_ShouldAddCustomerAndProductReadsForSalesOpportunity()
        {
            var result = PermissionResourceCatalog.ExpandDependencies(
            [
                new(PermissionResourceCatalog.SalesOpportunities, PermissionAction.Edit,
                    PermissionDataScope.Department)
            ]);

            AssertGrant(result, PermissionResourceCatalog.CrmCustomers, PermissionAction.View,
                PermissionDataScope.Department, "dependency");
            AssertGrant(result, PermissionModuleCatalog.CommonProductReference, PermissionAction.View,
                PermissionDataScope.Department, "dependency");
        }

        [Theory]
        [InlineData("unknown", PermissionAction.View, PermissionDataScope.Own)]
        [InlineData(PermissionResourceCatalog.CrmCustomers, "unknown", PermissionDataScope.Own)]
        [InlineData(PermissionResourceCatalog.CrmCustomers, PermissionAction.View, "unknown")]
        public void NormalizeGrant_ShouldRejectUnknownContractValues(
            string resourceKey,
            string action,
            string dataScope)
        {
            Assert.Throws<ArgumentException>(() => PermissionResourceCatalog.NormalizeGrant(
                new PermissionGrantRecord(resourceKey, action, dataScope)));
        }

        [Fact]
        public void SalesTemplate_ShouldUseLeastPrivilegeBusinessDefaults()
        {
            var sales = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Sales);
            var grants = sales.GetEffectivePermissions();

            AssertGrant(grants, PermissionResourceCatalog.CrmCustomers, PermissionAction.Edit,
                PermissionDataScope.Own, "template");
            AssertGrant(grants, PermissionResourceCatalog.Suppliers, PermissionAction.View,
                PermissionDataScope.Company, "template");
            Assert.DoesNotContain(grants, grant =>
                grant.ResourceKey == PermissionResourceCatalog.CrmCustomers &&
                grant.Action is PermissionAction.Delete or PermissionAction.Export);
            Assert.DoesNotContain(grants, grant => grant.ResourceKey is
                PermissionResourceCatalog.SystemUsers or
                PermissionResourceCatalog.SystemPermissions or
                PermissionResourceCatalog.SystemSettings or
                PermissionResourceCatalog.SystemAudit or
                PermissionResourceCatalog.SystemBackup or
                PermissionResourceCatalog.SystemDisasterRecovery or
                PermissionResourceCatalog.EmailPolicy);
            Assert.DoesNotContain(grants, grant =>
                grant.ResourceKey == PermissionResourceCatalog.EmailTemplates &&
                grant.Action == PermissionAction.Edit);
        }

        [Fact]
        public void SalesManagerTemplate_ShouldRemainDepartmentScoped()
        {
            var manager = BuiltInPermissionTemplateCatalog.Templates.Single(template =>
                template.Code == BuiltInPermissionTemplateCatalog.SalesManager);
            var grants = manager.GetEffectivePermissions();

            AssertGrant(grants, PermissionResourceCatalog.CrmCustomers, PermissionAction.Export,
                PermissionDataScope.Department, "template");
            AssertGrant(grants, PermissionResourceCatalog.SalesOpportunities, PermissionAction.Archive,
                PermissionDataScope.Department, "template");
            Assert.DoesNotContain(grants, grant => grant.DataScope == PermissionDataScope.All &&
                grant.ResourceKey.StartsWith("sales.", StringComparison.Ordinal));
        }

        [Fact]
        public void DisasterRecovery_ShouldRemainAdministratorIdentityOnly()
        {
            var resource = PermissionResourceCatalog.ByKey[PermissionResourceCatalog.SystemDisasterRecovery];
            var admin = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Admin);

            Assert.True(resource.IsTechnical);
            Assert.Contains(admin.Grants, grant =>
                grant.ResourceKey == PermissionResourceCatalog.SystemDisasterRecovery &&
                grant.Action == PermissionAction.Manage);
        }

        [Fact]
        public void ResourceCatalog_ShouldKeepUniqueKeysActionsAndStableSortOrder()
        {
            Assert.Equal(
                PermissionResourceCatalog.Resources.Count,
                PermissionResourceCatalog.Resources.Select(resource => resource.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(
                PermissionResourceCatalog.Resources.Select(resource => resource.SortOrder).Order().ToArray(),
                PermissionResourceCatalog.Resources.Select(resource => resource.SortOrder).ToArray());
            Assert.All(PermissionResourceCatalog.Resources, resource => Assert.Equal(
                resource.Actions.Count,
                resource.Actions.Select(action => action.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        }

        [Fact]
        public void ResourceCatalog_ShouldExposeOnlyImplementedSpecializedActions()
        {
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.CrmCustomers].Actions,
                action => action.Key == PermissionAction.Assign);
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.CrmFollowUps].Actions,
                action => action.Key == PermissionAction.Export);
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.SalesOpportunities].Actions,
                action => action.Key is PermissionAction.Assign or PermissionAction.Approve or PermissionAction.Import or PermissionAction.Export);
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.SalesQuotes].Actions,
                action => action.Key is PermissionAction.Approve or PermissionAction.Export);
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.ReportResources].Actions,
                action => action.Key == PermissionAction.Delete);
            Assert.DoesNotContain(PermissionResourceCatalog.ByKey[PermissionResourceCatalog.PaymentOutput].Actions,
                action => action.Key is PermissionAction.ExportZip or PermissionAction.SendEmail);
        }

        [Fact]
        public void ModuleCatalog_ShouldKeepUniqueKeysAndStableSortOrder()
        {
            Assert.Equal(
                PermissionModuleCatalog.Modules.Count,
                PermissionModuleCatalog.Modules.Select(module => module.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(
                PermissionModuleCatalog.Modules.Select(module => module.SortOrder).Order().ToArray(),
                PermissionModuleCatalog.Modules.Select(module => module.SortOrder).ToArray());
        }

        private static void AssertGrant(
            IEnumerable<EffectivePermissionGrant> grants,
            string resourceKey,
            string action,
            string dataScope,
            string source)
        {
            Assert.Contains(grants, grant =>
                grant.ResourceKey == resourceKey &&
                grant.Action == action &&
                grant.DataScope == dataScope &&
                grant.Source == source);
        }
    }
}
