using System.Text.Json;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly JsonSerializerOptions BackgroundJobRetryJsonOptions = new(JsonSerializerDefaults.Web);

        private static IResult WriteConflict(string message)
        {
            return Results.Json(
                new ApiErrorResponse(string.IsNullOrWhiteSpace(message) ? "操作失败。" : message),
                statusCode: StatusCodes.Status409Conflict);
        }

        private static IResult WriteForbidden(string message)
        {
            return Results.Json(
                new ApiErrorResponse(string.IsNullOrWhiteSpace(message) ? "没有权限执行该操作。" : message),
                statusCode: StatusCodes.Status403Forbidden);
        }

        private static async Task<User> FindUserByIdAsync(
            IUserService userService,
            int userId,
            CancellationToken cancellationToken)
        {
            var users = await userService.GetUsersAsync(cancellationToken);
            return users.FirstOrDefault(user => user.Id == userId)
                   ?? throw new InvalidOperationException("未找到已保存的用户。");
        }

        private static string SerializeBackgroundJobRetryRequest<TRequest>(TRequest request)
        {
            return JsonSerializer.Serialize(request, BackgroundJobRetryJsonOptions);
        }

        internal static IResult AcceptedBackgroundJob(BackgroundJobSnapshot job)
        {
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
            string safeFileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                string.Equals(safeFileName, ".", StringComparison.Ordinal) ||
                string.Equals(safeFileName, "..", StringComparison.Ordinal))
            {
                throw new ArgumentException("下载文件名无效。", nameof(fileName));
            }

            string directory = Path.Combine(pathProvider.ExportRoot, "Browser", safeKind, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, safeFileName);
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

            string root = Path.GetFullPath(Path.Combine(pathProvider.ExportRoot, "Browser"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileNamePart(string value)
        {
            var chars = value.Trim()
                .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
                .ToArray();
            return new string(chars).Trim('.', ' ');
        }
    }
}
