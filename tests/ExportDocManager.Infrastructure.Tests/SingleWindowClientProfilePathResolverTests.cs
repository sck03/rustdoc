using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Infrastructure.Tests
{
    public class SingleWindowClientProfilePathResolverTests
    {
        [Fact]
        public void GetBuiltInBusinessRoot_ShouldUseRuntimeSingleWindowRoot()
        {
            string singleWindowRoot = Path.Combine(@"D:\", "ExportDoc", "App_Data", "SingleWindow");
            const string profileKey = "SWP-11111111111111111111111111111111";

            string customsRoot = SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                singleWindowRoot,
                profileKey,
                SingleWindowBusinessType.CustomsCoo);
            string agentRoot = SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                singleWindowRoot,
                profileKey,
                SingleWindowBusinessType.AgentConsignment);

            Assert.Equal(Path.Combine(singleWindowRoot, "Client", "Profiles", profileKey, "CustomsCoo"), customsRoot);
            Assert.Equal(Path.Combine(singleWindowRoot, "Client", "Profiles", profileKey, "AgentConsignment"), agentRoot);
            Assert.DoesNotContain(@"C:\", customsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\", agentRoot, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveConfiguredRoot_ShouldKeepBusinessDirectoriesIndependent()
        {
            var profile = new SwClientProfile
            {
                CustomsCooClientRootPath = @"D:\SingleWindow\CustomsCoo",
                AgentConsignmentClientRootPath = @"D:\SingleWindow\AgentConsignment"
            };

            string customsRoot = SingleWindowClientProfilePathResolver.ResolveConfiguredRoot(
                profile,
                SingleWindowBusinessType.CustomsCoo);
            string agentRoot = SingleWindowClientProfilePathResolver.ResolveConfiguredRoot(
                profile,
                SingleWindowBusinessType.AgentConsignment);

            Assert.Equal(@"D:\SingleWindow\CustomsCoo", customsRoot);
            Assert.Equal(@"D:\SingleWindow\AgentConsignment", agentRoot);
            Assert.NotEqual(customsRoot, agentRoot);
        }

        [Fact]
        public void NormalizeClientRootPath_ShouldRejectNetworkShare()
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                SingleWindowClientProfilePathResolver.NormalizeClientRootPath(@"\\server\single-window"));

            Assert.Contains("本机磁盘", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void NormalizeClientRootPath_ShouldRejectRelativePath()
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                SingleWindowClientProfilePathResolver.NormalizeClientRootPath(
                    Path.Combine("OfficialClient", "CompanyA")));

            Assert.Contains("绝对路径", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GetBuiltInBusinessRoot_ShouldRejectInvalidProfileKey()
        {
            Assert.Throws<ArgumentException>(() =>
                SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                    @"D:\ExportDoc\App_Data\SingleWindow",
                    "..\\OtherCompany",
                    SingleWindowBusinessType.CustomsCoo));
        }
    }
}
