using ExportDocManager.Services.Security;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Tools;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapPdfToolEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/pdf/merge/save-to-path", (
                HttpContext context,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiPdfMergeRequest request) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("合并本机 PDF 仅支持桌面版；浏览器版请上传文件。");
                }

                var validation = ValidatePdfMergeRequest(request, out var sourceFiles, out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueuePdfMergeJob(jobRunner, user.Username, sourceFiles, destinationPath));
            })
            .WithName("StartPdfMergeSaveToPathJob")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/tools/pdf/merge/upload", async (
                HttpContext context,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner,
                IBusinessClock clock,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

                if (!context.Request.HasFormContentType)
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 合并上传必须使用 multipart/form-data。"));
                }

                var form = await context.Request.ReadFormAsync(cancellationToken);
                var files = form.Files.Where(file => file.Length > 0).ToArray();
                if (files.Length < 2 || files.Length > ApiUploadLimits.PdfMergeMaximumFileCount)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        $"请选择 2 至 {ApiUploadLimits.PdfMergeMaximumFileCount} 个 PDF 文件。"));
                }

                if (files.Any(file => !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.BadRequest(new ApiErrorResponse("PDF 合并只接受 .pdf 文件。"));
                }

                if (files.Any(file => file.Length > ApiUploadLimits.PdfMergeFileBytes))
                {
                    return WritePayloadTooLarge(ApiUploadLimits.PdfMergeFileBytes);
                }

                long uploadBytes = 0;
                foreach (var file in files)
                {
                    if (uploadBytes > ApiUploadLimits.PdfMergeBytes - file.Length)
                    {
                        return WritePayloadTooLarge(ApiUploadLimits.PdfMergeBytes);
                    }
                    uploadBytes += file.Length;
                }
                if (uploadBytes > ApiUploadLimits.PdfMergeBytes)
                {
                    return WritePayloadTooLarge(ApiUploadLimits.PdfMergeBytes);
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
                        await ApiUploadLimits.CopyFormFileAsync(
                            files[index],
                            output,
                            ApiUploadLimits.PdfMergeFileBytes,
                            cancellationToken);
                        sourceFiles.Add(sourcePath);
                    }

                    string destinationPath = CreateBrowserDownloadPath(pathProvider, "PdfMerge", $"merged-{clock.Now:yyyyMMdd-HHmmss}.pdf");
                    return AcceptedBackgroundJob(EnqueuePdfMergeJob(
                        jobRunner,
                        user.Username,
                        sourceFiles,
                        destinationPath,
                        deleteSourceDirectoryAfterCompletion: true,
                        enableRetry: false));
                }
                catch (PayloadLimitExceededException ex)
                {
                    TryDeleteDirectory(uploadRoot);
                    return WritePayloadTooLarge(ex);
                }
                catch
                {
                    TryDeleteDirectory(uploadRoot);
                    throw;
                }
            })
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithName("UploadAndStartPdfMergeDownloadJob")
            .Produces<BackgroundJobSnapshot>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        }

        internal static IResult? ValidatePdfMergeRequest(
            ApiPdfMergeRequest? request,
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

            if (files.Count < 2 || files.Count > ApiUploadLimits.PdfMergeMaximumFileCount)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    $"请选择 2 至 {ApiUploadLimits.PdfMergeMaximumFileCount} 个 PDF 源文件。"));
            }

            string? invalidSourceExtension = files.FirstOrDefault(file =>
                !string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(invalidSourceExtension))
            {
                return Results.BadRequest(new ApiErrorResponse($"源文件必须是 PDF：{invalidSourceExtension}"));
            }

            string? missingFile = files.FirstOrDefault(file => !File.Exists(file));
            if (!string.IsNullOrWhiteSpace(missingFile))
            {
                return Results.BadRequest(new ApiErrorResponse($"PDF 源文件不存在：{missingFile}"));
            }

            long totalBytes = 0;
            foreach (string file in files)
            {
                long length = new FileInfo(file).Length;
                if (length <= 0)
                {
                    return Results.BadRequest(new ApiErrorResponse($"PDF 源文件不能为空：{file}"));
                }
                if (length > ApiUploadLimits.PdfMergeFileBytes)
                {
                    return WritePayloadTooLarge(ApiUploadLimits.PdfMergeFileBytes);
                }
                if (totalBytes > ApiUploadLimits.PdfMergeBytes - length)
                {
                    return WritePayloadTooLarge(ApiUploadLimits.PdfMergeBytes);
                }
                totalBytes += length;
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
                if (sourceFiles.Contains(destinationPath, StringComparer.OrdinalIgnoreCase))
                {
                    sourceFiles = Array.Empty<string>();
                    destinationPath = string.Empty;
                    return Results.BadRequest(new ApiErrorResponse("PDF 输出文件不能覆盖任一源文件。"));
                }
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
                        EnsurePdfMergeResourceLimits(sourceFiles);
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
                    : string.Empty,
                initialOutputPath: destinationPath);
        }

        private static void EnsurePdfMergeResourceLimits(IReadOnlyCollection<string> sourceFiles)
        {
            if (sourceFiles == null ||
                sourceFiles.Count < 2 ||
                sourceFiles.Count > ApiUploadLimits.PdfMergeMaximumFileCount)
            {
                throw new ServiceValidationException(
                    $"PDF 合并必须包含 2 至 {ApiUploadLimits.PdfMergeMaximumFileCount} 个源文件。");
            }

            long totalBytes = 0;
            foreach (string sourceFile in sourceFiles)
            {
                if (!File.Exists(sourceFile))
                {
                    throw new FileNotFoundException("PDF 源文件不存在。", sourceFile);
                }

                long length = new FileInfo(sourceFile).Length;
                if (length <= 0)
                {
                    throw new ServiceValidationException($"PDF 源文件不能为空：{sourceFile}");
                }
                if (length > ApiUploadLimits.PdfMergeFileBytes)
                {
                    throw new PayloadLimitExceededException(ApiUploadLimits.PdfMergeFileBytes);
                }
                if (totalBytes > ApiUploadLimits.PdfMergeBytes - length)
                {
                    throw new PayloadLimitExceededException(ApiUploadLimits.PdfMergeBytes);
                }
                totalBytes += length;
            }
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
