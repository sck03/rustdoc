using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

/// <summary>
/// Converts infrastructure template paths into stable browser API identifiers.
/// Absolute server paths never cross the API boundary.
/// </summary>
internal static class ApiReportTemplatePathPolicy
{
    private const string BuiltInPrefix = "builtin:";
    private const string UserPrefix = "user:";
    private const string DatabaseTemplatePrefix = "user-template:";

    public static string ToClientPath(IAppPathProvider paths, string templatePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return string.Empty;
        }

        string normalized = templatePath.Trim();
        if (normalized.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(DatabaseTemplatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeSeparators(normalized);
        }

        try
        {
            string fullPath = Path.GetFullPath(normalized);
            string userRoot = Path.GetFullPath(paths.UserTemplateRoot);
            if (PathBoundaryHelper.IsWithinRoot(fullPath, userRoot))
            {
                return UserPrefix + NormalizeSeparators(Path.GetRelativePath(userRoot, fullPath));
            }

            string builtInRoot = Path.GetFullPath(paths.TemplateRoot);
            if (PathBoundaryHelper.IsWithinRoot(fullPath, builtInRoot))
            {
                return BuiltInPrefix + NormalizeSeparators(Path.GetRelativePath(builtInRoot, fullPath));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return string.Empty;
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', '/');
}

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static string ToApiReportTemplatePath(HttpContext context, string templatePath) =>
        ApiReportTemplatePathPolicy.ToClientPath(
            context.RequestServices.GetRequiredService<IAppPathProvider>(),
            templatePath);
}
