using System.IO.Compression;
using System.Text.Json;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ReportTemplateDomainIsolationTests
{
    [Fact]
    public void ExportGlobals_ShouldKeepSealDataInsideExportDocumentDomain()
    {
        string root = CreateTestRoot("export-seal-globals");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string sealRoot = Path.Combine(dataRoot, "Files", "Seals", "Exporters", "42");
        string documentSealPath = Path.Combine(sealRoot, "document.png");
        string customsSealPath = Path.Combine(sealRoot, "customs.png");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(sealRoot);
        File.WriteAllBytes(documentSealPath, OnePixelPng);
        File.WriteAllBytes(customsSealPath, OnePixelPng);

        try
        {
            var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(
                new Invoice(),
                new Customer(),
                new Exporter
                {
                    DocSealPath = "Files/Seals/Exporters/42/document.png",
                    CustomsSealPath = "Files/Seals/Exporters/42/customs.png"
                },
                withSeal: true,
                new RuntimeAppPathProvider(appRoot, dataRoot));

            Assert.True(globals.ContainsKey("ShowSeal"));
            Assert.True(globals.ContainsKey("doc_seal_path"));
            Assert.True(globals.ContainsKey("customs_seal_path"));
            Assert.StartsWith("data:image/png;base64,", Assert.IsType<string>(globals["doc_seal_path"]), StringComparison.Ordinal);
            Assert.StartsWith("data:image/png;base64,", Assert.IsType<string>(globals["customs_seal_path"]), StringComparison.Ordinal);
            Assert.False(globals.ContainsKey("Payment"));
            Assert.False(globals.ContainsKey("Payee"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ExportGlobals_ShouldRejectSealImagesOutsideManagedRoots()
    {
        string root = CreateTestRoot("external-export-seal-globals");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string externalSealPath = Path.Combine(root, "external-seals", "boen-baoguan.png");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(externalSealPath)!);
        File.WriteAllBytes(externalSealPath, OnePixelPng);

        try
        {
            var globals = ReportTemplateGlobalsBuilder.BuildInvoiceGlobals(
                new Invoice(),
                new Customer(),
                new Exporter
                {
                    DocSealPath = externalSealPath,
                    CustomsSealPath = externalSealPath
                },
                withSeal: true,
                new RuntimeAppPathProvider(appRoot, dataRoot));

            Assert.Equal(string.Empty, Assert.IsType<string>(globals["doc_seal_path"]));
            Assert.Equal(string.Empty, Assert.IsType<string>(globals["customs_seal_path"]));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void PaymentGlobals_ShouldContainOnlyPaymentDomainObjects()
    {
        var globals = ReportTemplateGlobalsBuilder.BuildPaymentVoucherGlobals(
            new Payment { PayerName = "付款公司" },
            new Payee { Name = "收款单位" });

        Assert.True(globals.ContainsKey("Payment"));
        Assert.True(globals.ContainsKey("Payee"));
        Assert.True(globals.ContainsKey("cny_amount_upper"));
        Assert.False(globals.ContainsKey("Invoice"));
        Assert.False(globals.ContainsKey("Customer"));
        Assert.False(globals.ContainsKey("Exporter"));
        Assert.False(globals.ContainsKey("ShowSeal"));
        Assert.False(globals.ContainsKey("doc_seal_path"));
        Assert.False(globals.ContainsKey("customs_seal_path"));
    }

    [Theory]
    [InlineData("{{ payer_seal_path }}")]
    [InlineData("{{ Payment.CustomsSealPath }}")]
    [InlineData("{{ Payment[\"CompanyStampPath\"] }}")]
    [InlineData("{{ ShowSeal }}")]
    public void PaymentTemplatePolicy_ShouldRejectEverySealReference(string content)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ReportTemplateContentPolicy.Validate(ReportDocumentType.PaymentVoucher, content));

        Assert.Contains("付款报销模板不提供印章数据", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltInPaymentTemplates_ShouldSaveAsUserCopiesWithoutExporterFields()
    {
        string repositoryRoot = FindRepositoryRoot();
        string root = CreateTestRoot("payment-built-ins");
        string dataRoot = Path.Combine(root, "data");
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(repositoryRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            string[] templatePaths = Directory.GetFiles(
                Path.Combine(repositoryRoot, "Templates", "Internal"),
                "*.html",
                SearchOption.TopDirectoryOnly);
            Assert.NotEmpty(templatePaths);

            foreach (string templatePath in templatePaths)
            {
                string content = await File.ReadAllTextAsync(templatePath);
                Assert.DoesNotContain("Exporter", content, StringComparison.Ordinal);
                Assert.Contains("Payment.PayerName", content, StringComparison.Ordinal);

                var saved = await service.SaveTemplateContentAsync(
                    ReportDocumentType.PaymentVoucher,
                    templatePath,
                    content);

                Assert.True(File.Exists(saved.TemplatePath));
                Assert.True(PathBoundaryHelper.IsWithinRoot(
                    saved.TemplatePath,
                    Path.Combine(dataRoot, "Templates", "Internal")));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("payment_voucher_template.html", "付款单")]
    [InlineData("expense_reimbursement_template.html", "费用报销单")]
    public async Task PaymentStarterTemplates_ShouldCreateAndSaveWithoutCrossDomainFields(
        string fileName,
        string displayName)
    {
        string root = CreateTestRoot($"payment-starter-{fileName}");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(appRoot);
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var created = await service.CreateTemplateAsync(
                ReportDocumentType.PaymentVoucher,
                $"Internal/{fileName}",
                displayName);

            Assert.Contains("Payment.PayerName", created.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Exporter", created.Content, StringComparison.Ordinal);
            Assert.Null(created.WithSealDefault);
            var saved = await service.SaveTemplateContentAsync(
                ReportDocumentType.PaymentVoucher,
                created.TemplatePath,
                created.Content);
            Assert.Equal(created.Content, saved.Content);
            Assert.Null(saved.WithSealDefault);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreateTemplateAsync_ShouldClassifyExistingTargetAsConflict()
    {
        string root = CreateTestRoot("template-create-conflict");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string targetPath = Path.Combine(dataRoot, "Templates", "Internal", "existing.html");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(targetPath, "<html><body>existing</body></html>");
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var error = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.CreateTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    "Internal/existing.html",
                    "已存在模板"));

            Assert.Equal("目标模板已存在。", error.Message);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RenameTemplateAsync_ShouldClassifyExistingTargetAsConflict()
    {
        string root = CreateTestRoot("template-rename-conflict");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string templateRoot = Path.Combine(dataRoot, "Templates", "Internal");
        string sourcePath = Path.Combine(templateRoot, "source.html");
        string targetPath = Path.Combine(templateRoot, "target.html");
        Directory.CreateDirectory(templateRoot);
        await File.WriteAllTextAsync(sourcePath, "<html><body>source</body></html>");
        await File.WriteAllTextAsync(targetPath, "<html><body>target</body></html>");
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var error = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                service.RenameTemplateAsync(
                    ReportDocumentType.PaymentVoucher,
                    "user:Internal/source.html",
                    "Internal/target.html"));

            Assert.Equal("目标模板已存在。", error.Message);
            Assert.True(File.Exists(sourcePath));
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

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
            Assert.True(exportConfig.WithSeal);
            Assert.Null(paymentConfig.WithSeal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task PaymentTemplatePreview_ShouldOmitSealMetadataEvenWhenRequested()
    {
        string root = CreateTestRoot("payment-preview-no-seal");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(appRoot);
        var service = new ReportTemplateService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var preview = await service.PreviewTemplateContentAsync(
                ReportDocumentType.PaymentVoucher,
                "<html><body>{{ Payment.PayerName }}</body></html>",
                withSeal: true);

            Assert.Null(preview.WithSeal);
            Assert.Contains("示例付款单位", preview.Html, StringComparison.Ordinal);
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
                new PaymentTemplateItem
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

    [Fact]
    public async Task PaymentTemplatePackageManifest_ShouldOmitSealProperties()
    {
        string root = CreateTestRoot("payment-package-no-seal");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string paymentTemplatePath = Path.Combine(dataRoot, "Templates", "Internal", "payment.html");
        Directory.CreateDirectory(Path.GetDirectoryName(paymentTemplatePath)!);
        await File.WriteAllTextAsync(paymentTemplatePath, "<html><body>{{ Payment.InvoiceNo }}</body></html>");

        var settings = new AppSettings
        {
            PaymentTemplates =
            [
                new PaymentTemplateItem
                {
                    Name = "付款模板",
                    TemplatePath = "user:Internal/payment.html",
                    ReportType = ReportDocumentType.PaymentVoucher.ToString(),
                    IsEnabled = true
                }
            ]
        };
        var service = new ReportTemplatePackageService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(settings));
        string packagePath = Path.Combine(dataRoot, "TemplatePackages", "payment.edtpl");

        try
        {
            await service.ExportAsync(packagePath);

            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("config.json");
            Assert.NotNull(manifestEntry);
            using var stream = manifestEntry.Open();
            using var document = await JsonDocument.ParseAsync(stream);

            var paymentManifestItem = Assert.Single(
                document.RootElement.GetProperty("InternalTemplates").EnumerateArray());
            Assert.False(paymentManifestItem.TryGetProperty("ShowSeal", out _));

            var templateRows = document.RootElement.GetProperty("Templates").EnumerateArray().ToArray();
            var paymentTemplateRow = Assert.Single(
                templateRows,
                item => string.Equals(
                    item.GetProperty("Type").GetString(),
                    ReportTemplateCatalogLoader.InternalTemplateCatalogType,
                    StringComparison.OrdinalIgnoreCase));
            Assert.False(paymentTemplateRow.TryGetProperty("WithSeal", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task PaymentTemplatePackageImport_ShouldRejectSealMetadataBeforeWritingFiles()
    {
        string root = CreateTestRoot("payment-package-import-seal-reject");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string packageSource = Path.Combine(root, "package-source");
        string packageTemplate = Path.Combine(packageSource, "Templates", "Internal", "payment.html");
        string packagePath = Path.Combine(root, "payment-with-seal.edtpl");
        Directory.CreateDirectory(Path.GetDirectoryName(packageTemplate)!);
        await File.WriteAllTextAsync(packageTemplate, "<html><body>{{ Payment.PayerName }}</body></html>");
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "config.json"),
            """
            {
              "PackageVersion": "1.1",
              "ExportedAt": "2026-07-29T00:00:00",
              "Templates": [
                {
                  "Type": "Internal",
                  "Name": "付款模板",
                  "FileName": "user:Internal/payment.html",
                  "WithSeal": false
                }
              ],
              "ExportTemplates": [],
              "InternalTemplates": [
                {
                  "Name": "付款模板",
                  "TemplatePath": "user:Internal/payment.html",
                  "ReportType": "PaymentVoucher",
                  "IsEnabled": true
                }
              ]
            }
            """);
        ZipFile.CreateFromDirectory(packageSource, packagePath);
        var service = new ReportTemplatePackageService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(packagePath));

            Assert.Contains("付款报销模板", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dataRoot, "Templates", "Internal", "payment.html")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TemplatePackageImport_ShouldRejectOldManifestBeforeWritingFiles()
    {
        string root = CreateTestRoot("template-package-old-schema-reject");
        string appRoot = Path.Combine(root, "app");
        string dataRoot = Path.Combine(root, "data");
        string packageSource = Path.Combine(root, "package-source");
        string packageTemplate = Path.Combine(packageSource, "Templates", "Export", "invoice.html");
        string packagePath = Path.Combine(root, "old-schema.edtpl");
        Directory.CreateDirectory(Path.GetDirectoryName(packageTemplate)!);
        await File.WriteAllTextAsync(packageTemplate, "<html><body>{{ Invoice.InvoiceNo }}</body></html>");
        await File.WriteAllTextAsync(
            Path.Combine(packageSource, "config.json"),
            """
            {
              "PackageVersion": "1.0",
              "ExportedAt": "2026-07-29T00:00:00",
              "Templates": [],
              "ExportTemplates": [],
              "InternalTemplates": []
            }
            """);
        ZipFile.CreateFromDirectory(packageSource, packagePath);
        var service = new ReportTemplatePackageService(
            new RuntimeAppPathProvider(appRoot, dataRoot),
            new StubSettingsService(new AppSettings()));

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(packagePath));

            Assert.Contains("当前仅接受 1.1", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dataRoot, "Templates", "Export", "invoice.html")));
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

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
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
