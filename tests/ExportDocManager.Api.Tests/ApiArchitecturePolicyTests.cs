namespace ExportDocManager.Api.Tests
{
    public class ApiArchitecturePolicyTests
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTokensByRelativePath =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hosting/ApiRuntimeOptions.cs"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "AppContext.BaseDirectory"
                }
            };

        private static readonly string[] ForbiddenTokens =
        [
            "System.Drawing",
            "System.Windows.Forms",
            "Windows.Forms",
            "Microsoft.Web.WebView2",
            "Path.GetTempPath",
            "Directory.GetCurrentDirectory",
            "Environment.GetFolderPath",
            "SpecialFolder",
            "CommonApplicationData",
            "LocalApplicationData",
            "ApplicationData",
            "ProgramData",
            @"C:\",
            "AppContext.BaseDirectory"
        ];

        [Fact]
        public void ApiSource_ShouldNotIntroduceDesktopOrSystemDiskDefaults()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            var violations = Directory
                .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .SelectMany(path => FindViolations(sourceRoot, path))
                .ToList();

            Assert.True(
                violations.Count == 0,
                "API architecture policy violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiEndpointCompositionRoot_ShouldOnlyDelegateToEndpointModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenCompositionTokens =
            [
                "endpoints.MapGet(",
                "endpoints.MapPost(",
                "endpoints.MapPut(",
                "endpoints.MapDelete(",
                "\"/api/",
                "\"/healthz",
                "\"/openapi",
                "\"/swagger"
            ];

            var violations = forbiddenCompositionTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiEndpointRouteBuilderExtensions.cs should delegate to endpoint modules, but contains `{token}`.")
                .ToList();

            Assert.Contains("MapExportDocManagerApiEndpoints", content, StringComparison.Ordinal);
            Assert.True(
                violations.Count == 0,
                "API endpoint composition root violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiToolEndpointCompositionRoot_ShouldOnlyDelegateToToolModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiToolEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenCompositionTokens =
            [
                "endpoints.MapGet(",
                "endpoints.MapPost(",
                "endpoints.MapPut(",
                "endpoints.MapDelete(",
                "\"/api/tools/"
            ];

            var violations = forbiddenCompositionTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiToolEndpointRouteBuilderExtensions.cs should delegate to tool modules, but contains `{token}`.")
                .ToList();

            foreach (string expectedDelegate in new[]
            {
                "MapPdfToolEndpoints",
                "MapLetterOfCreditToolEndpoints",
                "MapOcrToolEndpoints",
                "MapExchangeRateToolEndpoints",
                "MapEmailToolEndpoints",
                "MapContainerPackingToolEndpoints",
                "MapExcelToolEndpoints"
            })
            {
                Assert.Contains(expectedDelegate, content, StringComparison.Ordinal);
            }

            Assert.True(
                violations.Count == 0,
                "API tool endpoint composition root violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiToolExcelEndpointCompositionRoot_ShouldOnlyDelegateToExcelModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiToolExcelEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenCompositionTokens =
            [
                "endpoints.MapGet(",
                "endpoints.MapPost(",
                "endpoints.MapPut(",
                "endpoints.MapDelete(",
                "\"/api/tools/excel/"
            ];

            var violations = forbiddenCompositionTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiToolExcelEndpointRouteBuilderExtensions.cs should delegate to excel modules, but contains `{token}`.")
                .ToList();

            foreach (string expectedDelegate in new[]
            {
                "MapExcelImportPreviewEndpoint",
                "MapExcelTemplateExportEndpoint",
                "MapExcelBookingSheetEndpoints"
            })
            {
                Assert.Contains(expectedDelegate, content, StringComparison.Ordinal);
            }

            Assert.True(
                violations.Count == 0,
                "API tool Excel endpoint composition root violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiReportEndpointCompositionRoot_ShouldOnlyDelegateToReportModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiReportEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenCompositionTokens =
            [
                "endpoints.MapGet(",
                "endpoints.MapPost(",
                "endpoints.MapPut(",
                "endpoints.MapDelete(",
                "\"/api/reports/"
            ];

            var violations = forbiddenCompositionTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiReportEndpointRouteBuilderExtensions.cs should delegate to report modules, but contains `{token}`.")
                .ToList();

            foreach (string expectedDelegate in new[]
            {
                "MapReportTemplateEndpoints",
                "MapInvoiceReportHtmlPreviewEndpoints",
                "MapInvoiceDocumentPackageHtmlPreviewEndpoints",
                "MapPaymentDraftReportHtmlPreviewEndpoints",
                "MapPaymentReportHtmlPreviewEndpoints",
                "MapInvoiceReportPdfEndpoint",
                "MapPaymentReportPdfEndpoint",
                "MapInvoiceReportPdfZipEndpoint",
                "MapInvoiceDocumentPackageEndpoint",
                "MapInvoiceDocumentEmailEndpoint"
            })
            {
                Assert.Contains(expectedDelegate, content, StringComparison.Ordinal);
            }

            Assert.True(
                violations.Count == 0,
                "API report endpoint composition root violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiMasterDataEndpointCompositionRoot_ShouldDelegateExtractedResourceModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiMasterDataEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenExtractedResourceTokens =
            [
                "\"/api/master-data/customers",
                "\"/api/master-data/exporters",
                "\"/api/master-data/payees",
                "\"/api/master-data/products",
                "\"/api/master-data/ports",
                "\"/api/master-data/units",
                "\"/api/master-data/hs-codes"
            ];

            var violations = forbiddenExtractedResourceTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiMasterDataEndpointRouteBuilderExtensions.cs should delegate extracted resource modules, but contains `{token}`.")
                .ToList();

            Assert.Contains("MapCustomerMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapExporterMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapPayeeMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapProductMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapPortMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapUnitMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.Contains("MapHsCodeMasterDataEndpoints", content, StringComparison.Ordinal);
            Assert.True(
                violations.Count == 0,
                "API master-data endpoint composition violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiSingleWindowEndpointCompositionRoot_ShouldOnlyDelegateToSingleWindowModules()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string path = Path.Combine(sourceRoot, "Hosting", "ApiSingleWindowEndpointRouteBuilderExtensions.cs");
            string content = File.ReadAllText(path);

            string[] forbiddenCompositionTokens =
            [
                "endpoints.MapGet(",
                "endpoints.MapPost(",
                "endpoints.MapPut(",
                "endpoints.MapDelete(",
                "\"/api/single-window/"
            ];

            var violations = forbiddenCompositionTokens
                .Where(token => content.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiSingleWindowEndpointRouteBuilderExtensions.cs should delegate to single-window modules, but contains `{token}`.")
                .ToList();

            foreach (string expectedDelegate in new[]
            {
                "MapSingleWindowReferenceCatalogEndpoints",
                "MapSingleWindowIssuingAuthorityEndpoints",
                "MapSingleWindowCollaborationEndpoints",
                "MapSingleWindowProducerProfileEndpoints",
                "MapSingleWindowDocumentEndpoints",
                "MapSingleWindowPackageEndpoints",
                "MapSingleWindowClientEndpoints",
                "MapSingleWindowExportReviewEndpoints",
                "MapSingleWindowOperationCenterEndpoints"
            })
            {
                Assert.Contains(expectedDelegate, content, StringComparison.Ordinal);
            }

            Assert.True(
                violations.Count == 0,
                "API single-window endpoint composition root violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiBackgroundJobService_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            foreach (string expectedFile in new[]
            {
                "ApiBackgroundJobService.cs",
                "ApiBackgroundJobServiceQuery.cs",
                "ApiBackgroundJobServicePersistence.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing background job service module `{expectedFile}`.");
            }

            string serviceContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobService.cs"));
            foreach (string persistenceToken in new[]
            {
                "JsonSerializer",
                "AtomicFileHelper",
                "File.ReadAllText",
                "File.Exists",
                "WriteAllTextAtomic"
            })
            {
                Assert.DoesNotContain(persistenceToken, serviceContent, StringComparison.Ordinal);
            }

            string queryContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobServiceQuery.cs"));
            Assert.Contains("QueryAsync", queryContent, StringComparison.Ordinal);
            Assert.Contains("GetAsync", queryContent, StringComparison.Ordinal);

            string persistenceContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobServicePersistence.cs"));
            Assert.Contains("LoadPersistedJobs", persistenceContent, StringComparison.Ordinal);
            Assert.Contains("PersistJobs", persistenceContent, StringComparison.Ordinal);
            Assert.Contains("NormalizeRestoredJob", persistenceContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiBackgroundJobRunner_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            foreach (string expectedFile in new[]
            {
                "ApiBackgroundJobRunner.cs",
                "ApiBackgroundJobRunnerExecution.cs",
                "ApiBackgroundJobExecutionContext.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing background job runner module `{expectedFile}`.");
            }

            string runnerContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRunner.cs"));
            foreach (string executionToken in new[]
            {
                "private async Task RunAsync",
                "CreateScope",
                "LogError",
                "BackgroundJobStatusCatalog.Running",
                "BackgroundJobStatusCatalog.Succeeded",
                "BackgroundJobStatusCatalog.Failed",
                "BackgroundJobStatusCatalog.Canceled",
                "new ApiBackgroundJobExecutionContext("
            })
            {
                Assert.DoesNotContain(executionToken, runnerContent, StringComparison.Ordinal);
            }

            Assert.Contains("Enqueue(", runnerContent, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobStatusCatalog.Queued", runnerContent, StringComparison.Ordinal);

            string executionContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRunnerExecution.cs"));
            Assert.Contains("RunAsync(", executionContent, StringComparison.Ordinal);
            Assert.Contains("CreateScope", executionContent, StringComparison.Ordinal);
            Assert.Contains("HasRetryDescriptor", executionContent, StringComparison.Ordinal);
            Assert.Contains("LogError", executionContent, StringComparison.Ordinal);

            string contextContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobExecutionContext.cs"));
            Assert.Contains("class ApiBackgroundJobExecutionContext", contextContent, StringComparison.Ordinal);
            Assert.Contains("Report(", contextContent, StringComparison.Ordinal);
            Assert.Contains("BackgroundJobStatusCatalog.Canceling", contextContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiBackgroundJobRetryDispatcher_ShouldRemainSplitByJobArea()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            foreach (string expectedFile in new[]
            {
                "ApiBackgroundJobRetryDispatcher.cs",
                "ApiBackgroundJobRetryDispatcherPdf.cs",
                "ApiBackgroundJobRetryDispatcherExcel.cs",
                "ApiBackgroundJobRetryDispatcherReport.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing background job retry dispatcher module `{expectedFile}`.");
            }

            string dispatcherContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRetryDispatcher.cs"));
            foreach (string jobSpecificToken in new[]
            {
                "ApiPdfMergeRequest",
                "ApiExcelOutputRequest",
                "ApiExcelConvertBookingSheetRequest",
                "ApiInvoiceBookingSheetRequest",
                "ApiReportPdfRequest",
                "ApiInvoiceReportZipRequest",
                "ApiInvoiceDocumentPackageRequest",
                "ApiInvoiceDocumentEmailRequest",
                "ValidatePdfMergeRequest",
                "ValidateExcelDestinationPath",
                "ValidateReportPdfRequest",
                "ValidateInvoiceReportZipRequest",
                "ValidateInvoiceDocumentPackageRequest",
                "ValidateInvoiceDocumentEmailRequest",
                "EnqueuePdfMergeJob",
                "EnqueueExcelTemplateExportJob",
                "EnqueueInvoiceReportPdfJob",
                "EnqueueInvoiceReportPdfZipJob",
                "EnqueueInvoiceDocumentPackageJob",
                "EnqueueInvoiceDocumentEmailJob"
            })
            {
                Assert.DoesNotContain(jobSpecificToken, dispatcherContent, StringComparison.Ordinal);
            }

            string pdfContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRetryDispatcherPdf.cs"));
            Assert.Contains("RetryPdfMergeJob", pdfContent, StringComparison.Ordinal);

            string excelContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRetryDispatcherExcel.cs"));
            Assert.Contains("RetryExcelTemplateExportJob", excelContent, StringComparison.Ordinal);
            Assert.Contains("RetryInvoiceBookingSheetExportJobAsync", excelContent, StringComparison.Ordinal);

            string reportContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiBackgroundJobRetryDispatcherReport.cs"));
            Assert.Contains("RetryInvoiceReportPdfJob", reportContent, StringComparison.Ordinal);
            Assert.Contains("RetryPaymentVoucherPdfJob", reportContent, StringComparison.Ordinal);
            Assert.Contains("RetryInvoiceReportPdfZipJob", reportContent, StringComparison.Ordinal);
            Assert.Contains("RetryInvoiceDocumentPackageJob", reportContent, StringComparison.Ordinal);
            Assert.Contains("RetryInvoiceDocumentEmailJob", reportContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiSingleWindowEndpointHelpers_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiSingleWindowEndpointHelpers.cs")),
                "ApiSingleWindowEndpointHelpers.cs should remain split into responsibility-specific helper modules.");

            foreach (string expectedFile in new[]
            {
                "ApiSingleWindowCommonEndpointHelpers.cs",
                "ApiSingleWindowExportReviewEndpointHelpers.cs",
                "ApiSingleWindowPackageEndpointHelpers.cs",
                "ApiSingleWindowPackagePathHelpers.cs",
                "ApiSingleWindowPackageManifestHelpers.cs",
                "ApiSingleWindowClientEndpointHelpers.cs",
                "ApiSingleWindowDocumentEndpointHelpers.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing single-window helper module `{expectedFile}`.");
            }

            string packageHelperContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiSingleWindowPackageEndpointHelpers.cs"));
            foreach (string extractedToken in new[]
            {
                "Path.Combine(",
                "Path.GetInvalidFileNameChars",
                "ZipFile.OpenRead",
                "JsonSerializer.Deserialize<SingleWindowPackageManifest>"
            })
            {
                Assert.DoesNotContain(extractedToken, packageHelperContent, StringComparison.Ordinal);
            }

            string packagePathContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiSingleWindowPackagePathHelpers.cs"));
            Assert.Contains("BuildDefaultSingleWindowSubmitPackagePath", packagePathContent, StringComparison.Ordinal);
            Assert.Contains("BuildDefaultSingleWindowReceiptPackagePath", packagePathContent, StringComparison.Ordinal);
            Assert.Contains("BuildDefaultSingleWindowImportWorkingRoot", packagePathContent, StringComparison.Ordinal);
            Assert.Contains("BuildSafeSingleWindowFileToken", packagePathContent, StringComparison.Ordinal);

            string packageManifestContent = File.ReadAllText(Path.Combine(hostingRoot, "ApiSingleWindowPackageManifestHelpers.cs"));
            Assert.Contains("ReadSingleWindowPackageManifest", packageManifestContent, StringComparison.Ordinal);
            Assert.Contains("ZipFile.OpenRead", packageManifestContent, StringComparison.Ordinal);
            Assert.Contains("manifest.json", packageManifestContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiSingleWindowModels_ShouldRemainSplitByContractArea()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiSingleWindowModels.cs")),
                "ApiSingleWindowModels.cs should remain split into contract, document, and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiSingleWindowContractModels.cs",
                "ApiSingleWindowDocumentToolModels.cs",
                "ApiCustomsCooDocumentModels.cs",
                "ApiCustomsCooItemModels.cs",
                "ApiCustomsCooSupportingModels.cs",
                "ApiCustomsCooProducerProfileModels.cs",
                "ApiAgentConsignmentModels.cs",
                "ApiSingleWindowDtoFactory.cs",
                "ApiSingleWindowReferenceCatalogDtoFactory.cs",
                "ApiSingleWindowIssuingAuthorityDtoFactory.cs",
                "ApiSingleWindowPackageDtoFactory.cs",
                "ApiSingleWindowClientProfileDtoFactory.cs",
                "ApiCustomsCooDtoFactory.cs",
                "ApiCustomsCooProducerProfileDtoFactory.cs",
                "ApiAgentConsignmentDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing single-window model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiSingleWindowDtoFactory_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");
            string sharedFactoryPath = Path.Combine(hostingRoot, "ApiSingleWindowDtoFactory.cs");
            string sharedFactoryContent = File.ReadAllText(sharedFactoryPath);

            foreach (string expectedFile in new[]
            {
                "ApiSingleWindowReferenceCatalogDtoFactory.cs",
                "ApiSingleWindowIssuingAuthorityDtoFactory.cs",
                "ApiSingleWindowPackageDtoFactory.cs",
                "ApiSingleWindowClientProfileDtoFactory.cs",
                "ApiCustomsCooDtoFactory.cs",
                "ApiCustomsCooProducerProfileDtoFactory.cs",
                "ApiAgentConsignmentDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing single-window DTO factory module `{expectedFile}`.");
            }

            string[] forbiddenSharedFactoryTokens =
            [
                "FromReferenceCatalog",
                "FromIssuingAuthorityCatalog",
                "FromHandoffPackageResult",
                "FromReceiptPackageResult",
                "FromImportedPackage",
                "FromClientProfile",
                "FromSavedClientProfile",
                "FromCustomsCooDocument",
                "ToCustomsCooDocument",
                "FromCustomsCooProducerProfile",
                "ToCustomsCooProducerProfileInput",
                "FromAgentConsignmentDocument",
                "ToAgentConsignmentDocument"
            ];

            var violations = forbiddenSharedFactoryTokens
                .Where(token => sharedFactoryContent.Contains(token, StringComparison.Ordinal))
                .Select(token => $"ApiSingleWindowDtoFactory.cs should keep only shared helpers, but contains `{token}`.")
                .ToList();

            Assert.True(
                violations.Count == 0,
                "Single-window DTO factory responsibility violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void ApiCustomsCooModels_ShouldRemainSplitByDocumentArea()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiCustomsCooModels.cs")),
                "ApiCustomsCooModels.cs should remain split into document, item, and supporting contract modules.");

            foreach (string expectedFile in new[]
            {
                "ApiCustomsCooDocumentModels.cs",
                "ApiCustomsCooItemModels.cs",
                "ApiCustomsCooSupportingModels.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing Customs COO model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiToolModels_ShouldRemainSplitByToolArea()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiToolModels.cs")),
                "ApiToolModels.cs should remain split into tool-specific model modules.");

            foreach (string expectedFile in new[]
            {
                "ApiToolPdfModels.cs",
                "ApiToolLetterOfCreditModels.cs",
                "ApiToolOcrModels.cs",
                "ApiOcrDtoFactory.cs",
                "ApiToolExchangeRateModels.cs",
                "ApiToolExcelContractModels.cs",
                "ApiExcelDtoFactory.cs",
                "ApiToolContainerPackingContractModels.cs",
                "ApiContainerPackingDtoFactory.cs",
                "ApiContainerPackingProjectDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing tool model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiToolExcelModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiToolExcelModels.cs")),
                "ApiToolExcelModels.cs should remain split into Excel contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiToolExcelContractModels.cs",
                "ApiExcelDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing Excel tool model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiToolContainerPackingModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiToolContainerPackingModels.cs")),
                "ApiToolContainerPackingModels.cs should remain split into container packing contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiToolContainerPackingContractModels.cs",
                "ApiContainerPackingDtoFactory.cs",
                "ApiContainerPackingProjectDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing container packing tool model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiInvoiceModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiInvoiceModels.cs")),
                "ApiInvoiceModels.cs should remain split into shared response, invoice contract, and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiCommonModels.cs",
                "ApiInvoiceContractModels.cs",
                "ApiInvoiceDtoFactory.cs",
                "ApiInvoiceListDtoFactory.cs",
                "ApiInvoiceDetailDtoFactory.cs",
                "ApiInvoiceSaveDtoFactory.cs",
                "ApiInvoicePartySnapshotDtoFactory.cs",
                "ApiInvoiceProfitAnalysisModels.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing invoice model module `{expectedFile}`.");
            }

            string sharedFactory = File.ReadAllText(Path.Combine(hostingRoot, "ApiInvoiceDtoFactory.cs"));
            foreach (string businessMappingToken in new[]
            {
                "FromPagedInvoices",
                "FromInvoiceDetail",
                "ToInvoiceForSave",
                "CreateCustomerForAutoCreation",
                "CreateExporterForAutoCreation",
                "PreserveExistingOwnership"
            })
            {
                Assert.DoesNotContain(businessMappingToken, sharedFactory, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ApiPaymentModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiPaymentModels.cs")),
                "ApiPaymentModels.cs should remain split into payment contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiPaymentContractModels.cs",
                "ApiPaymentDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing payment model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiAuditLogModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiAuditLogModels.cs")),
                "ApiAuditLogModels.cs should remain split into audit log contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiAuditLogContractModels.cs",
                "ApiAuditLogDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing audit log model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiSettingsModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiSettingsModels.cs")),
                "ApiSettingsModels.cs should remain split into settings contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiSettingsContractModels.cs",
                "ApiSettingsDtoFactory.cs",
                "ApiSettingsResponseDtoFactory.cs",
                "ApiSettingsSaveDtoFactory.cs",
                "ApiSettingsRestartPolicy.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing settings model module `{expectedFile}`.");
            }

            string sharedFactory = File.ReadAllText(Path.Combine(hostingRoot, "ApiSettingsDtoFactory.cs"));
            foreach (string businessMappingToken in new[]
            {
                "FromSettings",
                "FromSavedSettings",
                "PrepareForSave",
                "CopyInto",
                "RequiresRestartForSystemSettingsChange",
                "CreateSanitizedSettings",
                "GetSecrets"
            })
            {
                Assert.DoesNotContain(businessMappingToken, sharedFactory, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ApiUserManagementModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiUserManagementModels.cs")),
                "ApiUserManagementModels.cs should remain split into user management contract and factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiUserManagementContractModels.cs",
                "ApiUserManagementDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing user management model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiAuthModels_ShouldRemainSplitByResponsibility()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiAuthModels.cs")),
                "ApiAuthModels.cs should remain split into auth contract and user DTO factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiAuthContractModels.cs",
                "ApiUserDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing auth model module `{expectedFile}`.");
            }
        }

        [Fact]
        public void ApiMasterDataModels_ShouldRemainSplitByResourceArea()
        {
            string sourceRoot = ResolveSourceRoot("src", "ExportDocManager.Api");
            string hostingRoot = Path.Combine(sourceRoot, "Hosting");

            Assert.False(
                File.Exists(Path.Combine(hostingRoot, "ApiMasterDataModels.cs")),
                "ApiMasterDataModels.cs should remain split into contract and resource-specific factory modules.");

            foreach (string expectedFile in new[]
            {
                "ApiMasterDataContractModels.cs",
                "ApiMasterDataDtoFactoryShared.cs",
                "ApiMasterDataCustomerDtoFactory.cs",
                "ApiMasterDataExporterDtoFactory.cs",
                "ApiMasterDataPayeeDtoFactory.cs",
                "ApiMasterDataProductDtoFactory.cs",
                "ApiMasterDataPortDtoFactory.cs",
                "ApiMasterDataUnitDtoFactory.cs",
                "ApiMasterDataHsCodeDtoFactory.cs"
            })
            {
                Assert.True(
                    File.Exists(Path.Combine(hostingRoot, expectedFile)),
                    $"Missing master-data model module `{expectedFile}`.");
            }
        }

        private static IEnumerable<string> FindViolations(string sourceRoot, string path)
        {
            string relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            string content = File.ReadAllText(path);
            AllowedTokensByRelativePath.TryGetValue(relativePath, out var allowedTokens);

            foreach (string token in ForbiddenTokens)
            {
                if (!content.Contains(token, StringComparison.Ordinal))
                {
                    continue;
                }

                if (allowedTokens?.Contains(token) == true)
                {
                    continue;
                }

                yield return $"{relativePath}: contains unreviewed token `{token}`";
            }
        }

        private static bool IsBuildOutput(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            return normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSourceRoot(params string[] segments)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException($"Could not locate {string.Join("/", segments)} from test output.");
        }
    }
}
