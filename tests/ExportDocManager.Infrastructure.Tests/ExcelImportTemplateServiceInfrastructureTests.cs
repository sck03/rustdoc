using ClosedXML.Excel;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Data;
using ExportDocManager.Services.Errors;
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
        public async Task ExportDefaultTemplate_ShouldQueryAtMostTwoDistinctNames()
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

                await service.ExportDefaultTemplateAsync(outputPath);

                using var exported = new XLWorkbook(outputPath);
                string exporterName = exported.Worksheet(1).Cell("A1").GetString();
                Assert.Equal(2, repository.LastDistinctNameLimit);
                Assert.True(ExcelImportTemplateService.IsTemplateExporterPlaceholder(exporterName));
            }
            finally
            {
                TryDeleteDirectory(appRoot);
                TryDeleteDirectory(dataRoot);
            }
        }

        [Fact]
        public void ExportBookingSheet_ShouldClassifySourceOverwriteAsValidationError()
        {
            string root = Path.Combine(Path.GetTempPath(), $"excel-booking-validation-{Guid.NewGuid():N}");
            string sourcePath = Path.Combine(root, "source.xlsx");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(sourcePath, "source-marker");
                var service = new ExcelImportTemplateService(
                    new StubSettingsService(),
                    new StubExporterReadRepository(),
                    new RuntimeAppPathProvider(root, Path.Combine(root, "data")));

                var error = Assert.Throws<ServiceValidationException>(() =>
                    service.ExportBookingSheet(sourcePath, sourcePath));

                Assert.Contains("不能覆盖源 Excel", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void ExportBookingSheet_ShouldClassifyExistingTargetAsConflict()
        {
            string root = Path.Combine(Path.GetTempPath(), $"excel-booking-conflict-{Guid.NewGuid():N}");
            string sourcePath = Path.Combine(root, "source.xlsx");
            string targetPath = Path.Combine(root, "target.xlsx");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(sourcePath, "source-marker");
                File.WriteAllText(targetPath, "target-marker");
                var service = new ExcelImportTemplateService(
                    new StubSettingsService(),
                    new StubExporterReadRepository(),
                    new RuntimeAppPathProvider(root, Path.Combine(root, "data")));

                var error = Assert.Throws<ResourceConflictException>(() =>
                    service.ExportBookingSheet(sourcePath, targetPath, overwrite: false));

                Assert.Contains("目标文件已存在", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(root);
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

            public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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

            public int LastDistinctNameLimit { get; private set; }

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

            public Task<IReadOnlyList<string>> QueryDistinctChineseNamesAsync(
                int maxCount = 2,
                CancellationToken cancellationToken = default)
            {
                LastDistinctNameLimit = maxCount;
                IReadOnlyList<string> result = _exporters
                    .Select(exporter => exporter?.ExporterNameCN?.Trim() ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Clamp(maxCount, 1, 10))
                    .ToArray();
                return Task.FromResult(result);
            }
        }
    }
}
