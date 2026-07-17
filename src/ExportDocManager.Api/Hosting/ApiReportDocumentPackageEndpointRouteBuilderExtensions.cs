using ExportDocManager.Services.Core;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private const string InvoiceDocumentPackageStoragePolicy =
            "单据包生成只读取当前发票/报关数据域和程序根 Templates 模板；临时 PDF 写运行数据根 Cache/ReportDocumentPackages 后自动清理；最终 ZIP 只写用户显式 .zip 路径，文件夹导出只写用户显式目录下按旧批量导出规则创建的批次文件夹，不读取付款/报销表，也不创建默认导出目录。";

        private static void MapInvoiceDocumentPackageEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/document-package/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId,
                ApiInvoiceDocumentPackageRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用单据包下载任务。");
                }

                var validation = ValidateInvoiceDocumentPackageRequest(
                    invoiceId,
                    request,
                    out var normalizedItems,
                    out bool includeMergedPdf,
                    out bool createZip,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueInvoiceDocumentPackageJob(
                    jobRunner,
                    user.Username,
                    invoiceId,
                    normalizedItems,
                    includeMergedPdf,
                    createZip,
                    destinationPath));
            })
            .WithName("StartInvoiceDocumentPackageSaveToPathJob");

            endpoints.MapPost("/api/reports/invoices/{invoiceId:int}/document-package/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                int invoiceId,
                ApiInvoiceDocumentPackageRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                request ??= new ApiInvoiceDocumentPackageRequest();
                request.CreateZip = true;
                request.DestinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "InvoiceDocumentPackage",
                    $"Invoice-{invoiceId}-Documents-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                var validation = ValidateInvoiceDocumentPackageRequest(
                    invoiceId,
                    request,
                    out var normalizedItems,
                    out bool includeMergedPdf,
                    out bool createZip,
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueInvoiceDocumentPackageJob(
                    jobRunner,
                    user.Username,
                    invoiceId,
                    normalizedItems,
                    includeMergedPdf,
                    createZip,
                    destinationPath));
            })
            .WithName("StartInvoiceDocumentPackageDownloadJob");
        }

        internal static IResult ValidateInvoiceDocumentPackageRequest(
            int invoiceId,
            ApiInvoiceDocumentPackageRequest request,
            out IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> normalizedItems,
            out bool includeMergedPdf,
            out bool createZip,
            out string destinationPath)
        {
            normalizedItems = Array.Empty<ApiInvoiceDocumentPackageItemRequest>();
            includeMergedPdf = true;
            createZip = true;
            destinationPath = string.Empty;

            if (invoiceId <= 0)
            {
                return Results.BadRequest(new ApiErrorResponse("发票ID必须大于0。"));
            }

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("单据包请求体不能为空。"));
            }

            var itemValidation = ValidateInvoiceDocumentItemRequests(request.Items, out var items);
            if (itemValidation != null)
            {
                return itemValidation;
            }

            string output = request.DestinationPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(output))
            {
                return Results.BadRequest(new ApiErrorResponse("单据包输出路径不能为空。"));
            }

            createZip = request.CreateZip;
            if (createZip && !string.Equals(Path.GetExtension(output), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("单据包输出路径必须以 .zip 结尾。"));
            }

            if (!createZip && string.Equals(Path.GetExtension(output), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("单据包文件夹输出目录不能是 .zip 文件。"));
            }

            try
            {
                normalizedItems = items;
                includeMergedPdf = request.IncludeMergedPdf;
                destinationPath = Path.GetFullPath(output);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"单据包输出路径无效：{ex.Message}"));
            }
        }

        internal static IResult ValidateInvoiceDocumentItemRequests(
            IReadOnlyCollection<ApiInvoiceDocumentPackageItemRequest> requestItems,
            out IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> normalizedItems)
        {
            normalizedItems = Array.Empty<ApiInvoiceDocumentPackageItemRequest>();
            var items = new List<ApiInvoiceDocumentPackageItemRequest>();
            foreach (var item in requestItems ?? Array.Empty<ApiInvoiceDocumentPackageItemRequest>())
            {
                if (item == null)
                {
                    continue;
                }

                if (!TryParseReportDocumentType(item.ReportType, out var reportType)
                    || reportType != ReportDocumentType.ExportDocument)
                {
                    return Results.BadRequest(new ApiErrorResponse("单据模板目前仅支持 ExportDocument 报表类型。"));
                }

                string templatePath = item.TemplatePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    return Results.BadRequest(new ApiErrorResponse("单据模板路径不能为空。"));
                }

                string name = item.Name?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Path.GetFileNameWithoutExtension(templatePath);
                }

                items.Add(new ApiInvoiceDocumentPackageItemRequest
                {
                    Name = name,
                    ReportType = reportType.ToString(),
                    TemplatePath = templatePath,
                    WithSeal = item.WithSeal
                });
            }

            if (items.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("请至少选择一个单据模板。"));
            }

            if (items.Count > 20)
            {
                return Results.BadRequest(new ApiErrorResponse("单次最多支持 20 个单据模板。"));
            }

            normalizedItems = items;
            return null;
        }

        internal static BackgroundJobSnapshot EnqueueInvoiceDocumentPackageJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            int invoiceId,
            IReadOnlyList<ApiInvoiceDocumentPackageItemRequest> items,
            bool includeMergedPdf,
            bool createZip,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "ReportDocumentPackage",
                createZip ? "发票单据包 ZIP 导出" : "发票单据文件夹导出",
                requestedBy,
                async (provider, jobContext) =>
                {
                    var pathProvider = provider.GetRequiredService<IAppPathProvider>();
                    string tempRoot = Path.Combine(
                        pathProvider.CacheRoot,
                        "ReportDocumentPackages",
                        jobContext.JobId);

                    try
                    {
                        var documentSet = await GenerateInvoiceDocumentPdfFilesAsync(
                                provider,
                                jobContext,
                                invoiceId,
                                items,
                                tempRoot,
                                includeMergedPdf,
                                10,
                                createZip ? 82 : 78,
                                destinationPath)
                            .ConfigureAwait(false);

                        if (!createZip)
                        {
                            string batchDirectory = await CopyInvoiceDocumentSetToExportFolderAsync(
                                    provider,
                                    jobContext,
                                    documentSet,
                                    destinationPath,
                                    82,
                                    98)
                                .ConfigureAwait(false);

                            jobContext.Report(
                                99,
                                "单据文件夹已保存",
                                Path.GetFileName(batchDirectory),
                                batchDirectory);

                            return batchDirectory;
                        }

                        var entries = documentSet.Entries
                            .Select(entry => (entry.SourcePath, entry.EntryName))
                            .ToList();

                        jobContext.Report(
                            88,
                            "正在生成单据包 ZIP",
                            Path.GetFileName(destinationPath),
                            destinationPath);

                        var zipProgress = new Progress<OperationProgressUpdate>(update =>
                        {
                            jobContext.Report(
                                update.ProgressPercent,
                                update.StatusText,
                                update.DetailText,
                                destinationPath);
                        });

                        await ZipArchiveHelper.CreateFromFilesAsync(
                                entries,
                                destinationPath,
                                jobContext.CancellationToken,
                                zipProgress,
                                "正在生成单据包 ZIP",
                                88,
                                98,
                                "当前没有需要打包的单据 PDF。")
                            .ConfigureAwait(false);

                        jobContext.Report(
                            99,
                            "正在保存单据包",
                            Path.GetFileName(destinationPath),
                            destinationPath);

                        return destinationPath;
                    }
                    finally
                    {
                        AtomicFileHelper.TryDeleteDirectory(tempRoot);
                    }
                },
                retryOperation: "StartInvoiceDocumentPackageJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiInvoiceDocumentPackageJobRetryRequest
                {
                    InvoiceId = invoiceId,
                    Body = new ApiInvoiceDocumentPackageRequest
                    {
                        Items = items.ToList(),
                        IncludeMergedPdf = includeMergedPdf,
                        CreateZip = createZip,
                        DestinationPath = destinationPath
                    }
                }));
        }

        internal static async Task<string> CopyInvoiceDocumentSetToExportFolderAsync(
            IServiceProvider provider,
            ApiBackgroundJobExecutionContext jobContext,
            ApiInvoiceGeneratedDocumentSet documentSet,
            string outputDirectory,
            int startProgress,
            int endProgress)
        {
            ArgumentNullException.ThrowIfNull(documentSet);

            var settingsService = provider.GetRequiredService<ISettingsService>();
            await settingsService.LoadAsync().ConfigureAwait(false);
            string folderPattern = settingsService.Settings?.BatchExport?.OutputFolderPattern
                ?? BatchExportPathHelper.DefaultFolderPattern;

            Directory.CreateDirectory(outputDirectory);
            string batchDirectory = BatchExportPathHelper.BuildBatchDirectory(
                outputDirectory,
                folderPattern,
                documentSet.InvoiceNo,
                documentSet.CustomerName,
                documentSet.ExportDate);
            Directory.CreateDirectory(batchDirectory);

            var entries = documentSet.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.SourcePath))
                .ToList();
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("当前没有需要导出的单据 PDF。");
            }

            int range = Math.Max(1, endProgress - startProgress);
            for (var index = 0; index < entries.Count; index++)
            {
                jobContext.CancellationToken.ThrowIfCancellationRequested();
                var entry = entries[index];
                string safeEntryName = Path.GetFileName(entry.EntryName);
                if (string.IsNullOrWhiteSpace(safeEntryName))
                {
                    safeEntryName = Path.GetFileName(entry.SourcePath);
                }

                string targetPath = BuildUniqueDocumentExportPath(batchDirectory, safeEntryName);
                int progress = startProgress + (int)Math.Round(index * (double)range / entries.Count);
                jobContext.Report(
                    progress,
                    "正在保存单据 PDF",
                    safeEntryName,
                    targetPath);

                await FileCopyHelper.CopyAsync(
                        entry.SourcePath,
                        targetPath,
                        overwrite: false,
                        jobContext.CancellationToken)
                    .ConfigureAwait(false);
            }

            return batchDirectory;
        }

        private static string BuildUniqueDocumentExportPath(string directory, string fileName)
        {
            string normalizedFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                normalizedFileName = "document.pdf";
            }

            string name = Path.GetFileNameWithoutExtension(normalizedFileName);
            string extension = Path.GetExtension(normalizedFileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".pdf";
            }

            string candidate = Path.Combine(directory, $"{name}{extension}");
            var index = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{name}_{index}{extension}");
                index++;
            }

            return candidate;
        }

        internal sealed class ApiInvoiceDocumentPackageJobRetryRequest
        {
            public int InvoiceId { get; set; }

            public ApiInvoiceDocumentPackageRequest Body { get; set; } = new();
        }
    }
}
