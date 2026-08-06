using ExportDocManager.Services.Security;

namespace ExportDocManager.Application.Tests
{
    public class PermissionModuleCatalogTests
    {
        [Fact]
        public void ExpandDependencies_ShouldAddPaymentSupportingCapabilities()
        {
            var result = PermissionModuleCatalog.ExpandDependencies(
            [
                new(PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.Operate)
            ]);

            Assert.Equal(PermissionAccessLevel.Operate, result[PermissionModuleCatalog.DocumentPayments]);
            Assert.Equal(PermissionAccessLevel.Operate, result[PermissionModuleCatalog.DocumentPaymentReports]);
            Assert.Equal(PermissionAccessLevel.Operate, result[PermissionModuleCatalog.DocumentCustomOptions]);
            Assert.Equal(PermissionAccessLevel.View, result[PermissionModuleCatalog.DocumentReferenceData]);
        }

        [Fact]
        public void ExpandDependencies_ShouldLimitQueryReferenceDataToView()
        {
            var result = PermissionModuleCatalog.ExpandDependencies(
            [
                new(PermissionModuleCatalog.DocumentQuery, PermissionAccessLevel.Manage)
            ]);

            Assert.Equal(PermissionAccessLevel.Manage, result[PermissionModuleCatalog.DocumentQuery]);
            Assert.Equal(PermissionAccessLevel.View, result[PermissionModuleCatalog.DocumentReferenceData]);
        }

        [Fact]
        public void ExpandDependencies_ShouldPreserveExplicitHigherTechnicalAccess()
        {
            var result = PermissionModuleCatalog.ExpandDependencies(
            [
                new(PermissionModuleCatalog.DocumentPayments, PermissionAccessLevel.View),
                new(PermissionModuleCatalog.DocumentReferenceData, PermissionAccessLevel.Manage)
            ]);

            Assert.Equal(PermissionAccessLevel.Manage, result[PermissionModuleCatalog.DocumentReferenceData]);
        }

        [Theory]
        [InlineData(PermissionAccessLevel.View, PermissionAccessLevel.View)]
        [InlineData(PermissionAccessLevel.Operate, PermissionAccessLevel.Operate)]
        [InlineData(PermissionAccessLevel.Manage, PermissionAccessLevel.Operate)]
        public void ExpandDependencies_ShouldAddMasterDataReadAndCandidateCapabilities(
            string masterDataAccess,
            string expectedCustomOptionAccess)
        {
            var result = PermissionModuleCatalog.ExpandDependencies(
            [
                new(PermissionModuleCatalog.DocumentMasterData, masterDataAccess)
            ]);

            Assert.Equal(masterDataAccess, result[PermissionModuleCatalog.DocumentMasterData]);
            Assert.Equal(PermissionAccessLevel.View, result[PermissionModuleCatalog.DocumentReferenceData]);
            Assert.Equal(PermissionAccessLevel.View, result[PermissionModuleCatalog.CommonProductReference]);
            Assert.Equal(expectedCustomOptionAccess, result[PermissionModuleCatalog.DocumentCustomOptions]);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentHsKnowledge, result.Keys);
        }

        [Theory]
        [InlineData(PermissionModuleCatalog.DocumentInvoices)]
        [InlineData(PermissionModuleCatalog.SalesOpportunities)]
        [InlineData(PermissionModuleCatalog.SalesSuppliers)]
        public void ExpandDependencies_ShouldAddSharedProductReferenceWithoutGrantingMasterDataMaintenance(
            string businessModule)
        {
            var result = PermissionModuleCatalog.ExpandDependencies(
            [
                new(businessModule, PermissionAccessLevel.Manage)
            ]);

            Assert.Equal(PermissionAccessLevel.View, result[PermissionModuleCatalog.CommonProductReference]);
            Assert.DoesNotContain(PermissionModuleCatalog.DocumentMasterData, result.Keys);
        }

        [Fact]
        public void FinanceTemplate_ShouldExpandTechnicalModulesWithoutDeclaringThemAsBusinessNavigation()
        {
            var finance = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Finance);
            var grants = finance.GetModuleAccess();

            Assert.DoesNotContain(
                finance.ModuleKeys,
                moduleKey => PermissionModuleCatalog.ByKey[moduleKey].IsTechnical);
            Assert.Equal(PermissionAccessLevel.Manage, grants[PermissionModuleCatalog.DocumentReports]);
            Assert.Equal(PermissionAccessLevel.Manage, grants[PermissionModuleCatalog.DocumentPaymentReports]);
            Assert.Equal(PermissionAccessLevel.Operate, grants[PermissionModuleCatalog.DocumentCustomOptions]);
            Assert.Equal(PermissionAccessLevel.View, grants[PermissionModuleCatalog.DocumentReferenceData]);
        }

        [Fact]
        public void DocumentTemplate_ShouldKeepHsKnowledgeReadOnlyAlongsideScopedMasterDataMaintenance()
        {
            var document = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Document);
            var grants = document.GetModuleAccess();

            Assert.Equal(PermissionAccessLevel.View, grants[PermissionModuleCatalog.DocumentHsKnowledge]);
            Assert.Equal(PermissionAccessLevel.Manage, grants[PermissionModuleCatalog.DocumentMasterData]);
            Assert.Contains(PermissionModuleCatalog.DocumentHsKnowledge, document.ModuleKeys);
            Assert.Contains(PermissionModuleCatalog.DocumentMasterData, document.ModuleKeys);
        }

        [Fact]
        public void DocumentTemplate_ShouldExposeDeclarationDictionaryAsAnIndependentModule()
        {
            var document = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Document);
            var grants = document.GetModuleAccess();

            Assert.Contains(PermissionModuleCatalog.DocumentDeclarationDictionary, document.ModuleKeys);
            Assert.Equal(PermissionAccessLevel.Operate, grants[PermissionModuleCatalog.DocumentDeclarationDictionary]);
            Assert.NotEqual(PermissionModuleCatalog.DocumentSingleWindow, PermissionModuleCatalog.DocumentDeclarationDictionary);
        }

        [Fact]
        public void DisasterRecovery_ShouldRemainTechnicalAndAdminManaged()
        {
            var module = PermissionModuleCatalog.ByKey[PermissionModuleCatalog.SystemDisasterRecovery];
            var admin = BuiltInPermissionTemplateCatalog.FindForRole(BuiltInPermissionTemplateCatalog.Admin);
            var grants = admin.GetModuleAccess();

            Assert.True(module.IsTechnical);
            Assert.Equal("系统", module.Group);
            Assert.Equal(PermissionAccessLevel.Manage, grants[PermissionModuleCatalog.SystemDisasterRecovery]);
            Assert.Contains(PermissionModuleCatalog.SystemDisasterRecovery, admin.ModuleKeys);
        }

        [Fact]
        public void ModuleCatalog_ShouldKeepUniqueKeysAndStableSortOrder()
        {
            Assert.Equal(
                PermissionModuleCatalog.Modules.Count,
                PermissionModuleCatalog.Modules
                    .Select(module => module.Key)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
            Assert.Equal(
                PermissionModuleCatalog.Modules.Select(module => module.SortOrder).Order().ToArray(),
                PermissionModuleCatalog.Modules.Select(module => module.SortOrder).ToArray());
        }
    }
}
