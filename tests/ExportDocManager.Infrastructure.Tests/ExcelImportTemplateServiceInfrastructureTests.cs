using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests
{
    public class ExcelImportTemplateServiceInfrastructureTests
    {
        [Fact]
        public void EnsureDefaultTemplateAvailable_ShouldResolveTemplateFromAppRootResources()
        {
            var appRoot = Path.Combine(Path.GetTempPath(), $"excel-template-app-{Guid.NewGuid():N}");
            var dataRoot = Path.Combine(Path.GetTempPath(), $"excel-template-data-{Guid.NewGuid():N}");
            var templatePath = Path.Combine(appRoot, "Resources", "ExcelTemplates", "invoice-import-template.xlsx");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
                File.WriteAllText(templatePath, "template-marker");

                var pathProvider = new RuntimeAppPathProvider(appRoot, dataRoot);
                var service = new ExcelImportTemplateService(
                    new StubSettingsService(),
                    new StubExporterReadRepository(),
                    pathProvider);

                var resolvedPath = service.EnsureDefaultTemplateAvailable();

                Assert.Equal(Path.GetFullPath(templatePath), resolvedPath);
                Assert.StartsWith(Path.GetFullPath(appRoot), resolvedPath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain($"{Path.DirectorySeparatorChar}App_Data{Path.DirectorySeparatorChar}", resolvedPath);
                Assert.DoesNotContain(Path.GetFullPath(dataRoot), resolvedPath, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                TryDeleteDirectory(appRoot);
                TryDeleteDirectory(dataRoot);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private sealed class StubSettingsService : ISettingsService
        {
            public AppSettings Settings { get; } = new();

            public Task LoadAsync() => Task.CompletedTask;

            public Task SaveAsync() => Task.CompletedTask;
        }

        private sealed class StubExporterReadRepository : IExporterReadRepository
        {
            public Task<IReadOnlyList<Exporter>> QueryAsync(
                ExporterReadQuery query,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<Exporter>>([]);
            }
        }
    }
}
