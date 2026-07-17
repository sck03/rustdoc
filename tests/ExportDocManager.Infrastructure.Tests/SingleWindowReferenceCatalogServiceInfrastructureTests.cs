using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Infrastructure.Tests
{
    public class SingleWindowReferenceCatalogServiceInfrastructureTests
    {
        [Fact]
        public async Task LoadAndSaveCatalog_ShouldUseAppRootResourcesAndDataRootOverride()
        {
            string appRoot = CreateTempDirectory("single-window-catalog-app");
            string dataRoot = CreateTempDirectory("single-window-catalog-data");
            string bundledPath = Path.Combine(appRoot, "Resources", "SingleWindow", "singlewindow_reference_catalogs.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
                await File.WriteAllTextAsync(
                    bundledPath,
                    JsonSerializer.Serialize(new SingleWindowReferenceCatalogModel
                    {
                        Ports =
                        [
                            new SingleWindowReferencePortEntry
                            {
                                Value = "BUNDLED PORT",
                                Aliases = ["内置港"]
                            }
                        ]
                    }));

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new SingleWindowReferenceCatalogService(pathProvider);

                var bundledCatalog = await service.LoadEffectiveCatalogAsync();
                string overridePath = service.GetOverrideCatalogPath();

                Assert.Equal("BUNDLED PORT", Assert.Single(bundledCatalog.Ports).Value);
                Assert.Equal(
                    Path.Combine(pathProvider.SingleWindowRoot, "singlewindow_reference_catalogs.override.json"),
                    overridePath);
                Assert.StartsWith(Path.GetFullPath(appRoot), bundledPath, StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith(Path.GetFullPath(dataRoot), overridePath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($"{Path.DirectorySeparatorChar}App_Data{Path.DirectorySeparatorChar}", bundledPath);

                await service.SaveOverrideCatalogAsync(new SingleWindowReferenceCatalogModel
                {
                    Ports =
                    [
                        new SingleWindowReferencePortEntry
                        {
                            Value = "OVERRIDE PORT",
                            Aliases = ["覆盖港"]
                        }
                    ]
                });

                Assert.True(File.Exists(overridePath));
                var effectiveCatalog = await service.LoadEffectiveCatalogAsync();
                Assert.Equal("OVERRIDE PORT", Assert.Single(effectiveCatalog.Ports).Value);
            }
            finally
            {
                DeleteDirectory(appRoot);
                DeleteDirectory(dataRoot);
            }
        }

        private static string CreateTempDirectory(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
