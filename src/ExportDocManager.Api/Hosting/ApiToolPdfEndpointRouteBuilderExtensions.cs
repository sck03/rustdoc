using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPdfToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/pdf/merge/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiPdfMergeRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("PDF 路径合并仅允许可信 Tauri 桌面端。浏览器端不接受服务器文件路径。");
                }

                var validation = ValidatePdfMergeRequest(request, out var sourceFiles, out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueuePdfMergeJob(jobRunner, user.Username, sourceFiles, destinationPath));
            })
            .WithName("StartPdfMergeSaveToPathJob");

            endpoints.MapPost("/api/tools/pdf/merge/upload", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!context.Request.HasFormContentType)
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 合并上传必须使用 multipart/form-data。"));
                }

                var form = await context.Request.ReadFormAsync(cancellationToken);
                var files = form.Files.Where(file => file.Length > 0).ToArray();
                if (files.Length < 2 || files.Length > 50)
                {
                    return Results.BadRequest(new ApiErrorResponse("请选择 2 至 50 个 PDF 文件。"));
                }

                if (files.Any(file => !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 合并只接受 .pdf 文件。"));
                }

                if (files.Sum(file => file.Length) > 100_000_000)
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 文件总大小不能超过 100 MB。"));
                }

                string uploadRoot = Path.Combine(pathProvider.CacheRoot, "BrowserUploads", "PdfMerge", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uploadRoot);
                try
                {
                    var sourceFiles = new List<string>();
                    for (int index = 0; index < files.Length; index++)
                    {
                        string fileName = $"{index + 1:000}-{Path.GetFileName(files[index].FileName)}";
                        string sourcePath = Path.Combine(uploadRoot, fileName);
                        await using var output = File.Create(sourcePath);
                        await files[index].CopyToAsync(output, cancellationToken);
                        sourceFiles.Add(sourcePath);
                    }

                    string destinationPath = CreateBrowserDownloadPath(pathProvider, "PdfMerge", $"merged-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
                    return AcceptedBackgroundJob(EnqueuePdfMergeJob(
                        jobRunner,
                        user.Username,
                        sourceFiles,
                        destinationPath,
                        deleteSourceDirectoryAfterCompletion: true,
                        enableRetry: false));
                }
                catch
                {
                    TryDeleteDirectory(uploadRoot);
                    throw;
                }
            })
            .WithName("UploadAndStartPdfMergeDownloadJob");
        }

        internal static IResult ValidatePdfMergeRequest(
            ApiPdfMergeRequest request,
            out IReadOnlyCollection<string> sourceFiles,
            out string destinationPath)
        {
            sourceFiles = Array.Empty<string>();
            destinationPath = string.Empty;

            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("PDF 合并请求体不能为空。"));
            }

            var files = (request.SourceFiles ?? new List<string>())
                .Select(file => file?.Trim() ?? string.Empty)
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("请至少选择一个 PDF 源文件。"));
            }

            string invalidSourceExtension = files.FirstOrDefault(file =>
                !string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(invalidSourceExtension))
            {
                return Results.BadRequest(new ApiErrorResponse($"源文件必须是 PDF：{invalidSourceExtension}"));
            }

            string missingFile = files.FirstOrDefault(file => !File.Exists(file));
            if (!string.IsNullOrWhiteSpace(missingFile))
            {
                return Results.BadRequest(new ApiErrorResponse($"PDF 源文件不存在：{missingFile}"));
            }

            string output = request.DestinationPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(output))
            {
                return Results.BadRequest(new ApiErrorResponse("PDF 输出路径不能为空。"));
            }

            if (!string.Equals(Path.GetExtension(output), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse("PDF 输出路径必须以 .pdf 结尾。"));
            }

            try
            {
                destinationPath = Path.GetFullPath(output);
                sourceFiles = files.Select(Path.GetFullPath).ToList();
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"PDF 路径无效：{ex.Message}"));
            }
        }

        internal static BackgroundJobSnapshot EnqueuePdfMergeJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            IReadOnlyCollection<string> sourceFiles,
            string destinationPath,
            bool deleteSourceDirectoryAfterCompletion = false,
            bool enableRetry = true)
        {
            return jobRunner.Enqueue(
                "PdfMerge",
                "PDF 合并",
                requestedBy,
                (provider, jobContext) =>
                {
                    try
                    {
                        jobContext.Report(
                            10,
                            "正在合并 PDF",
                            $"正在合并 {sourceFiles.Count} 个 PDF 文件。",
                            destinationPath);

                        var pdfMergeService = provider.GetRequiredService<IPdfMergeService>();
                        pdfMergeService.Merge(sourceFiles, destinationPath, jobContext.CancellationToken);
                        jobContext.Report(95, "正在保存 PDF", Path.GetFileName(destinationPath), destinationPath);
                        return Task.FromResult(destinationPath);
                    }
                    finally
                    {
                        if (deleteSourceDirectoryAfterCompletion)
                        {
                            string sourceDirectory = Path.GetDirectoryName(sourceFiles.FirstOrDefault() ?? string.Empty) ?? string.Empty;
                            TryDeleteDirectory(sourceDirectory);
                        }
                    }
                },
                retryOperation: enableRetry ? "StartPdfMergeJob" : string.Empty,
                retryRequestJson: enableRetry
                    ? SerializeBackgroundJobRetryRequest(new ApiPdfMergeRequest
                    {
                        SourceFiles = sourceFiles.ToList(),
                        DestinationPath = destinationPath
                    })
                    : string.Empty);
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch
            {
                // Browser upload cleanup is best effort.
            }
        }
    }
}
