using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.SingleWindow;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SingleWindowClientProfileServiceTests
    {
        [Fact]
        public async Task Profiles_ShouldSupportMultipleCompanyCardsAndSingleActiveSelection()
        {
            string root = CreateTempRoot();
            try
            {
                using var factory = new InMemoryTestDatabase();
                var service = CreateService(factory, root);

                await service.SaveAsync(CreateUpdate("公司 A / 卡 A", "公司 A", "CARD-A"));
                var firstActive = await service.GetActiveAsync();
                await service.SaveAsync(CreateUpdate("公司 B / 卡 B", "公司 B", "CARD-B"));
                await service.SaveAsync(CreateUpdate("公司 C / 卡 C", "公司 C", "CARD-C"));

                var profiles = await service.ListAsync();
                Assert.Equal(3, profiles.Count);
                var thirdActive = Assert.Single(profiles, profile => profile.IsActive);
                Assert.Equal("公司 C", thirdActive.CompanyScope);
                Assert.NotEqual(firstActive.ProfileKey, thirdActive.ProfileKey);
                Assert.NotEqual(
                    firstActive.CustomsCooClientRootPath,
                    thirdActive.CustomsCooClientRootPath);
                Assert.Equal(3, profiles.Select(profile => profile.CardIdentifier).Distinct().Count());
                Assert.All(profiles, profile =>
                {
                    Assert.NotEmpty(profile.ProtectedHandoffSecret);
                    Assert.StartsWith("SWAC1.", profile.StationAssignmentCode, StringComparison.Ordinal);
                    var assignment = SingleWindowStationAssignmentCode.Decode(profile.StationAssignmentCode);
                    Assert.Equal(profile.StationKey, assignment.StationKey);
                    Assert.Equal(profile.ProfileKey, assignment.ProfileKey);
                    Assert.Equal(profile.CompanyScope, assignment.CompanyScope);
                    Assert.Equal(profile.CardIdentifier, assignment.CardIdentifier);
                });

                await service.ActivateAsync(firstActive.ProfileKey);

                var reactivated = await service.GetActiveAsync();
                Assert.Equal(firstActive.ProfileKey, reactivated.ProfileKey);
                Assert.Equal("CARD-A", reactivated.CardIdentifier);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task ConcurrentProfileSaves_ShouldStillLeaveExactlyOneActiveProfile()
        {
            string root = CreateTempRoot();
            try
            {
                using var factory = new InMemoryTestDatabase();
                var service = CreateService(factory, root);
                var saves = Enumerable.Range(1, 6)
                    .Select(index => service.SaveAsync(CreateUpdate(
                        $"公司 {index} / 卡 {index}",
                        $"公司 {index}",
                        $"CARD-{index}")))
                    .ToArray();

                await Task.WhenAll(saves);

                var profiles = await service.ListAsync();
                Assert.Equal(6, profiles.Count);
                Assert.Single(profiles, profile => profile.IsActive);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task Save_ShouldRejectDirectoryOverlapAcrossBusinessTypesAndProfiles()
        {
            string root = CreateTempRoot();
            try
            {
                using var factory = new InMemoryTestDatabase();
                var service = CreateService(factory, root);
                string sharedRoot = Path.Combine(root, "OfficialClient", "CompanyA");

                var sameProfileError = await Assert.ThrowsAsync<ServiceValidationException>(() =>
                    service.SaveAsync(CreateUpdate(
                        "公司 A / 卡 A",
                        "公司 A",
                        "CARD-A",
                        sharedRoot,
                        Path.Combine(sharedRoot, "Acd"))));
                Assert.Contains("互相包含", sameProfileError.Message, StringComparison.Ordinal);

                await service.SaveAsync(CreateUpdate(
                    "公司 A / 卡 A",
                    "公司 A",
                    "CARD-A",
                    Path.Combine(root, "OfficialClient", "CompanyA-Coo"),
                    Path.Combine(root, "OfficialClient", "CompanyA-Acd")));

                var crossProfileError = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                    service.SaveAsync(CreateUpdate(
                        "公司 B / 卡 B",
                        "公司 B",
                        "CARD-B",
                        Path.Combine(root, "OfficialClient", "CompanyA-Coo", "Nested"),
                        Path.Combine(root, "OfficialClient", "CompanyB-Acd"))));
                Assert.Contains("不同公司和操作卡必须使用独立目录", crossProfileError.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task Save_ShouldLockCompanyAndCardIdentityImmediatelyAfterProfileCreation()
        {
            string root = CreateTempRoot();
            try
            {
                using var factory = new InMemoryTestDatabase();
                var service = CreateService(factory, root);
                await service.SaveAsync(CreateUpdate("公司 A / 卡 A", "公司 A", "CARD-A"));
                var profile = await service.GetActiveAsync();

                var identityChange = CreateUpdate(
                    "公司 A / 卡 A（修改身份）",
                    "公司 B",
                    "CARD-B",
                    profile.CustomsCooClientRootPath,
                    profile.AgentConsignmentClientRootPath,
                    profile.ProfileKey);
                var error = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                    service.SaveAsync(identityChange));

                Assert.Contains("请新增档案", error.Message, StringComparison.Ordinal);

                var renameOnly = CreateUpdate(
                    "公司 A / 卡 A（重命名）",
                    profile.CompanyScope,
                    profile.CardIdentifier,
                    profile.CustomsCooClientRootPath,
                    profile.AgentConsignmentClientRootPath,
                    profile.ProfileKey);
                await service.SaveAsync(renameOnly);

                var renamed = await service.GetActiveAsync();
                Assert.Equal("公司 A / 卡 A（重命名）", renamed.ProfileName);
                Assert.Equal("公司 A", renamed.CompanyScope);
                Assert.Equal("CARD-A", renamed.CardIdentifier);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task PostgreSqlMode_ShouldRejectStationProfiles()
        {
            string root = CreateTempRoot();
            try
            {
                using var factory = new InMemoryTestDatabase();
                var pathProvider = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                var service = new SingleWindowClientProfileService(
                    factory,
                    new SingleWindowStationIdentityService(pathProvider),
                    pathProvider,
                    new DatabaseConnectionSettings
                    {
                        Provider = DatabaseConnectionSettings.PostgreSqlProvider
                    });

                var error = await Assert.ThrowsAsync<ServiceValidationException>(() => service.ListAsync());
                Assert.Contains("SQLite", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task StationIdentity_ShouldRejectCorruptedIdentityInsteadOfRebindingProfiles()
        {
            string root = CreateTempRoot();
            try
            {
                var pathProvider = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                string identityPath = Path.Combine(
                    pathProvider.SecurityRoot,
                    "SingleWindow",
                    "station.id");
                Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
                const string corruptedIdentity = "LEGACY-STATION-123";
                await File.WriteAllTextAsync(identityPath, corruptedIdentity);

                var service = new SingleWindowStationIdentityService(pathProvider);
                var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                    service.GetCurrentStationKeyAsync());

                Assert.Contains("已损坏", error.Message, StringComparison.Ordinal);
                Assert.Equal(corruptedIdentity, await File.ReadAllTextAsync(identityPath));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static SingleWindowClientProfileService CreateService(
            IDbContextFactory<AppDbContext> factory,
            string root)
        {
            var pathProvider = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            return new SingleWindowClientProfileService(
                factory,
                new SingleWindowStationIdentityService(pathProvider),
                pathProvider,
                new DatabaseConnectionSettings());
        }

        private static SingleWindowClientProfileUpdate CreateUpdate(
            string profileName,
            string companyScope,
            string cardIdentifier,
            string customsCooRoot = "",
            string agentConsignmentRoot = "",
            string profileKey = "")
        {
            return new SingleWindowClientProfileUpdate
            {
                ProfileKey = profileKey,
                ProfileName = profileName,
                CompanyScope = companyScope,
                CardIdentifier = cardIdentifier,
                CustomsCooClientRootPath = customsCooRoot,
                AgentConsignmentClientRootPath = agentConsignmentRoot,
                CanSubmitCustomsCoo = true,
                CanSubmitAgentConsignment = true
            };
        }

        private static string CreateTempRoot()
        {
            string path = Path.Combine(Path.GetTempPath(), $"edm-station-profiles-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

    }
}
