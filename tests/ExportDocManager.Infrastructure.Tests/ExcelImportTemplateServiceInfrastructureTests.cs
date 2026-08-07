using ClosedXML.Excel;
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

        [Fact]
        public void ExportDefaultTemplate_ShouldInspectAllExportersBeforeSelectingUniqueName()
        {
            var appRoot = Path.Combine(Path.GetTempPath(), $"excel-template-app-{Guid.NewGuid():N}");
            var dataRoot = Path.Combine(Path.GetTempPath(), $"excel-template-data-{Guid.NewGuid():N}");
            var templatePath = Path.Combine(appRoot, "Resources", "ExcelTemplates", "invoice-import-template.xlsx");
            var outputPath = Path.Combine(dataRoot, "exported-template.xlsx");
            var exporters = Enumerable.Range(1, 201)
                .Select(index => new Exporter
                {
                    Id = index,
                    ExporterNameCN = index <= 200 ? "宁波甲出口有限公司" : "宁波乙出口有限公司"
                })
                .ToArray();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
                using (var template = new XLWorkbook())
                {
                    template.AddWorksheet("导入模板");
                    template.SaveAs(templatePath);
                }

                var repository = new StubExporterReadRepository(exporters);
                var service = new ExcelImportTemplateService(
                    new StubSettingsService(),
                    repository,
                    new RuntimeAppPathProvider(appRoot, dataRoot));

                service.ExportDefaultTemplate(outputPath);

                using var exported = new XLWorkbook(outputPath);
                string exporterName = exported.Worksheet(1).Cell("A1").GetString();
                Assert.True(repository.LastQuery.ReturnAll);
                Assert.True(ExcelImportTemplateService.IsTemplateExporterPlaceholder(exporterName));
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
            private readonly IReadOnlyList<Exporter> _exporters;

            public StubExporterReadRepository()
                : this([])
            {
            }

            public StubExporterReadRepository(IReadOnlyList<Exporter> exporters)
            {
                _exporters = exporters;
            }

            public ExporterReadQuery LastQuery { get; private set; } = new();

            public Task<IReadOnlyList<Exporter>> QueryAsync(
                ExporterReadQuery query,
                CancellationToken cancellationToken = default)
            {
                LastQuery = query;
                IReadOnlyList<Exporter> result = query.ReturnAll
                    ? _exporters
                    : _exporters.Take(Math.Clamp(query.MaxCount, 1, 500)).ToArray();
                return Task.FromResult(result);
            }

            public Task<PagedResult<Exporter>> QueryPageAsync(
                ExporterReadQuery query,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new PagedResult<Exporter>([], 0, 1, 50));
            }
        }
    }
}
