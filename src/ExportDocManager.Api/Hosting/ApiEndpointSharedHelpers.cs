using System.Text.Json;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Core;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly JsonSerializerOptions BackgroundJobRetryJsonOptions = JsonSerializerOptions.Web;

        private static IResult WriteConflict(string message)
        {
            return Results.Json(
                new ApiErrorResponse(string.IsNullOrWhiteSpace(message) ? "操作失败。" : message),
                statusCode: StatusCodes.Status409Conflict);
        }

        private static IResult WriteServiceException(Exception exception) =>
            ApiServiceExceptionMapper.ToResult(exception);

        private static IResult WriteValidation(string message) =>
            WriteServiceException(new ServiceValidationException(message));

        private static IResult WriteNotFound(string message) =>
            WriteServiceException(new ResourceNotFoundException(message));

        private static IResult WriteInfrastructureFailure(string message, Exception exception) =>
            WriteServiceException(new InfrastructureServiceException(message, exception));

        private static IResult WriteInvoiceSaveFailure(SaveResult result)
        {
            string message = string.IsNullOrWhiteSpace(result?.ErrorMessage) ? "保存发票失败。" : result.ErrorMessage;
            return result?.FailureKind switch
            {
                SaveFailureKind.Validation => Results.BadRequest(new ApiErrorResponse(message)),
                SaveFailureKind.Forbidden => WriteForbidden(message),
                SaveFailureKind.Conflict => WriteConflict(message),
                SaveFailureKind.Infrastructure => WriteInfrastructureFailure(
                    "发票保存服务暂时不可用，请稍后重试。",
                    new InfrastructureServiceException(message)),
                _ => Results.Json(new ApiErrorResponse("保存发票失败，请稍后重试。"), statusCode: StatusCodes.Status500InternalServerError)
            };
        }

        private static IResult WritePayloadTooLarge(PayloadLimitExceededException exception)
        {
            return Results.Json(
                new ApiErrorResponse(exception?.Message ?? "上传内容超过允许大小。"),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        private static IResult WritePayloadTooLarge(long maximumBytes) =>
            WritePayloadTooLarge(new PayloadLimitExceededException(maximumBytes));

        private static IResult WriteForbidden(string message)
        {
            return Results.Json(
                new ApiErrorResponse(string.IsNullOrWhiteSpace(message) ? "没有权限执行该操作。" : message),
                statusCode: StatusCodes.Status403Forbidden);
        }

        private static JsonHttpResult<ApiErrorResponse> TypedForbidden(string message) =>
            TypedResults.Json(
                new ApiErrorResponse(string.IsNullOrWhiteSpace(message) ? "没有权限执行该操作。" : message),
                statusCode: StatusCodes.Status403Forbidden);

        private static async Task<User> FindUserByIdAsync(
            IUserService userService,
            int userId,
            CancellationToken cancellationToken)
        {
            var users = await userService.GetUsersAsync(cancellationToken);
            return users.FirstOrDefault(user => user.Id == userId)
                   ?? throw new ResourceNotFoundException("未找到已保存的用户。");
        }

        private static string SerializeBackgroundJobRetryRequest<TRequest>(TRequest request)
        {
            return JsonSerializer.Serialize(request, BackgroundJobRetryJsonOptions);
        }

        internal static IResult AcceptedBackgroundJob(BackgroundJobSnapshot job)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (string.Equals(job.StatusText, ApiBackgroundJobQueueStatusCatalog.Rejected, StringComparison.Ordinal))
            {
                return Results.Json(
                    new ApiErrorResponse(job.ErrorMessage),
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return Results.Accepted($"/api/jobs/{job.JobId}", job);
        }

        internal static string CreateBrowserDownloadPath(
            IAppPathProvider pathProvider,
            string kind,
            string fileName)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            string safeKind = SanitizeFileNamePart(kind);
            if (string.IsNullOrWhiteSpace(safeKind) ||
                string.Equals(safeKind, ".", StringComparison.Ordinal) ||
                string.Equals(safeKind, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException("下载类型目录名无效。", nameof(kind));
            }

            string safeFileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                string.Equals(safeFileName, ".", StringComparison.Ordinal) ||
                string.Equals(safeFileName, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException("下载文件名无效。", nameof(fileName));
            }

            string browserRoot = Path.GetFullPath(Path.Combine(pathProvider.ExportRoot, "Browser"));
            string directory = PathBoundaryHelper.EnsureWithinRoot(
                Path.Combine(browserRoot, safeKind, Guid.NewGuid().ToString("N")),
                browserRoot,
                "受控浏览器下载目录超出允许范围。");
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                directory,
                pathProvider.DataRoot,
                "受控浏览器下载目录无效。");
            Directory.CreateDirectory(directory);
            PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                directory,
                pathProvider.DataRoot,
                "受控浏览器下载目录无效。");
            return PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                Path.Combine(directory, safeFileName),
                pathProvider.DataRoot,
                "受控浏览器下载文件路径无效。");
        }

        internal static bool IsControlledBrowserDownloadPath(
            IAppPathProvider pathProvider,
            string path)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }

            string root = Path.GetFullPath(Path.Combine(pathProvider.ExportRoot, "Browser"));
            if (!PathBoundaryHelper.IsWithinRoot(candidate, root))
            {
                return false;
            }

            try
            {
                PathBoundaryHelper.EnsureNoReparsePointsWithinRoot(
                    candidate,
                    pathProvider.DataRoot,
                    "受控浏览器下载文件路径无效。");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static IResult StreamTemporaryFile(
            HttpContext context,
            string filePath,
            string contentType,
            string downloadFileName,
            string cleanupDirectory = "")
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            context.Response.RegisterForDispose(new TemporaryDownloadCleanup(filePath, cleanupDirectory));
            return Results.File(
                filePath,
                contentType,
                downloadFileName,
                enableRangeProcessing: true);
        }

        private static string SanitizeFileNamePart(string value)
        {
            string normalized = CrossPlatformFileNamePolicy.ReplaceInvalidCharacters(value.Trim(), '_');
            return normalized.Trim('.', ' ');
        }

        private sealed class TemporaryDownloadCleanup : IDisposable
        {
            private readonly string filePath;
            private readonly string cleanupDirectory;

            public TemporaryDownloadCleanup(string filePath, string cleanupDirectory)
            {
                this.filePath = filePath;
                this.cleanupDirectory = cleanupDirectory ?? string.Empty;
            }

            public void Dispose()
            {
                if (!string.IsNullOrWhiteSpace(cleanupDirectory))
                {
                    AtomicFileHelper.TryDeleteDirectory(cleanupDirectory);
                    return;
                }

                AtomicFileHelper.TryDeleteFile(filePath);
            }
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;

            public InlineProgress(Action<T> report)
            {
                this.report = report ?? throw new ArgumentNullException(nameof(report));
            }

            public void Report(T value) => report(value);
        }
    }
}
