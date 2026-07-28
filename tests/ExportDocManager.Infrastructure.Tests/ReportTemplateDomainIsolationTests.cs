using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ReportTemplateDomainIsolationTests
{
    [Fact]
    public void Catalog_ShouldDeriveBusinessDomainFromManagedDirectory()
    {
        string root = CreateTestRoot("catalog");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string exportPath = Path.Combine(appRoot, "Templates", "Export", "invoice_template.html");
        string paymentPath = Path.Combine(appRoot, "Templates", "Internal", "payment_voucher_template.html");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paymentPath)!);
        File.WriteAllText(exportPath, "<html></html>");
        File.WriteAllText(paymentPath, "<html></html>");

        try
        {
            var resolver = new ReportTemplatePathResolver(new RuntimeAppPathProvider(appRoot, dataRoot));
            var loader = new ReportTemplateCatalogLoader(resolver);
            var configs = loader.BuildResolvedTemplateConfigs(
            [
                new ReportTemplateConfig { Type = "Internal", FileName = exportPath },
                new ReportTemplateConfig { Type = "Export", FileName = paymentPath }
            ]);

            var exportConfig = Assert.Single(configs, item => item.FileName == exportPath);
            var paymentConfig = Assert.Single(configs, item => item.FileName == paymentPath);
            Assert.Equal(ReportDocumentType.ExportDocument, ReportTemplateCatalogLoader.ResolveCatalogReportType(exportConfig.Type, exportConfig.FileName));
            Assert.Equal(ReportDocumentType.PaymentVoucher, ReportTemplateCatalogLoader.ResolveCatalogReportType(paymentConfig.Type, paymentConfig.FileName));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(ReportDocumentType.ExportDocument, "Export", "invoice_template.html")]
    [InlineData(ReportDocumentType.PaymentVoucher, "Internal", "payment_voucher_template.html")]
    public async Task SaveTemplateContentAsync_ShouldUpdateOnlyMatchingBusinessTemplateSettings(
        ReportDocumentType reportType,
        string category,
        string fileName)
    {
        string root = CreateTestRoot(reportType.ToString());
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string builtInPath = Path.Combine(appRoot, "Templates", category, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(builtInPath)!);
        await File.WriteAllTextAsync(builtInPath, "<html><body>original</body></html>");

        var settings = new AppSettings
        {
            BatchExport = new BatchExportSettings
            {
                Items =
                [
                    new BatchExportItem
                    {
                        Name = "出口单证模板",
                        TemplatePath = builtInPath,
                        ReportType = ReportDocumentType.ExportDocument.ToString()
                    }
                ]
            },
            PaymentTemplates =
            [
                new BatchExportItem
                {
                    Name = "付款报销模板",
                    TemplatePath = builtInPath,
                    ReportType = ReportDocumentType.PaymentVoucher.ToString()
                }
            ]
        };
        var settingsService = new StubSettingsService(settings);
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            settingsService);

        try
        {
            var result = await service.SaveTemplateContentAsync(
                reportType,
                builtInPath,
                "<html><body>updated</body></html>");

            string expectedStoredPath = $"user:{category}/{fileName}";
            Assert.Equal(Path.Combine(dataRoot, "Templates", category, fileName), result.TemplatePath);
            if (reportType == ReportDocumentType.PaymentVoucher)
            {
                Assert.Equal(builtInPath, settings.BatchExport.Items.Single().TemplatePath);
                Assert.Equal(expectedStoredPath, settings.PaymentTemplates.Single().TemplatePath);
            }
            else
            {
                Assert.Equal(expectedStoredPath, settings.BatchExport.Items.Single().TemplatePath);
                Assert.Equal(builtInPath, settings.PaymentTemplates.Single().TemplatePath);
            }

            Assert.Equal(1, settingsService.SaveCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTestRoot(string suffix)
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            ".codex-runtime",
            "report-template-domain-isolation-tests",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "ExportDocManager.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln from test output.");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public StubSettingsService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; }

        public int SaveCount { get; private set; }

        public Task LoadAsync() => Task.CompletedTask;

        public Task SaveAsync()
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
