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

            string customsRoot = SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                singleWindowRoot,
                SingleWindowBusinessType.CustomsCoo);
            string agentRoot = SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                singleWindowRoot,
                SingleWindowBusinessType.AgentConsignment);

            Assert.Equal(Path.Combine(singleWindowRoot, "Client", "Cooimp"), customsRoot);
            Assert.Equal(Path.Combine(singleWindowRoot, "Client", "Acd"), agentRoot);
            Assert.DoesNotContain(@"C:\", customsRoot, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\", agentRoot, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveEffectiveImportRoot_WhenProfilePathMissing_ShouldUseRuntimeSingleWindowRootFallback()
        {
            string singleWindowRoot = Path.Combine(@"D:\", "ExportDoc", "App_Data", "SingleWindow");
            var profile = new SwClientProfile();

            var resolved = SingleWindowClientProfilePathResolver.ResolveEffectiveImportRoot(
                profile,
                SingleWindowBusinessType.AgentConsignment,
                singleWindowRoot);

            Assert.Equal(Path.Combine(singleWindowRoot, "Client", "Acd"), resolved.Path);
        }
    }
}
