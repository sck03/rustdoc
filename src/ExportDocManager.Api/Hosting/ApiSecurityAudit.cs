using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

internal sealed record ApiEndpointSecurityAuditMetadata(string Category);

internal static class ApiSecurityAuditEndpointExtensions
{
    public static TBuilder WithApiSecurityAudit<TBuilder>(
        this TBuilder builder,
        string category)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return builder.WithMetadata(new ApiEndpointSecurityAuditMetadata(category.Trim()));
    }
}

internal sealed class ApiSecurityAuditWriter
{
    private const long MaximumAuditFileBytes = 8L * 1024 * 1024;
    private const int RetainedAuditFileCount = 5;
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
    private readonly Lock _writeGate = new();
    private readonly IAppPathProvider _pathProvider;

    public ApiSecurityAuditWriter(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public string AuditPath => Path.Combine(
        _pathProvider.LogRoot,
        "Security",
        "api-maintenance.jsonl");

    public void Write(ApiSecurityAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string auditPath = AuditPath;
        string auditRoot = Path.GetDirectoryName(auditPath)
            ?? throw new InvalidOperationException("无法解析 API 安全审计目录。");
        string line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

        lock (_writeGate)
        {
            Directory.CreateDirectory(auditRoot);
            EnsureRegularPath(auditRoot, expectDirectory: true);
            RuntimeFilePermissionHelper.RestrictDirectory(auditRoot);
            RotateIfRequired(auditPath, Encoding.UTF8.GetByteCount(line));
            File.AppendAllText(
                auditPath,
                line,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            RuntimeFilePermissionHelper.RestrictFile(auditPath);
        }
    }

    private static void RotateIfRequired(string auditPath, int pendingBytes)
    {
        if (!File.Exists(auditPath))
        {
            return;
        }

        EnsureRegularPath(auditPath, expectDirectory: false);
        if (new FileInfo(auditPath).Length + pendingBytes <= MaximumAuditFileBytes)
        {
            return;
        }

        for (int index = RetainedAuditFileCount; index >= 1; index--)
        {
            string sourcePath = index == 1
                ? auditPath
                : GetArchivePath(auditPath, index - 1);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            EnsureRegularPath(sourcePath, expectDirectory: false);
            string destinationPath = GetArchivePath(auditPath, index);
            if (File.Exists(destinationPath))
            {
                EnsureRegularPath(destinationPath, expectDirectory: false);
            }

            File.Move(sourcePath, destinationPath, overwrite: true);
            RuntimeFilePermissionHelper.RestrictFile(destinationPath);
        }
    }

    private static void EnsureRegularPath(string path, bool expectDirectory)
    {
        FileAttributes attributes = File.GetAttributes(path);
        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        if ((attributes & FileAttributes.ReparsePoint) != 0 || isDirectory != expectDirectory)
        {
            throw new InvalidOperationException("API 安全审计路径不能是符号链接、重解析点或错误的文件类型。");
        }
    }

    private static string GetArchivePath(string auditPath, int index) =>
        Path.Combine(
            Path.GetDirectoryName(auditPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(auditPath)}.{index}{Path.GetExtension(auditPath)}");
}

internal sealed record ApiSecurityAuditRecord(
    DateTimeOffset TimestampUtc,
    string Category,
    string Phase,
    string Method,
    string Route,
    int? UserId,
    string Username,
    string RemoteAddress,
    string CorrelationId,
    int? StatusCode,
    long? ElapsedMilliseconds);

internal sealed class ApiSecurityAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiSecurityAuditWriter _writer;
    private readonly ILogger<ApiSecurityAuditMiddleware> _logger;

    public ApiSecurityAuditMiddleware(
        RequestDelegate next,
        ApiSecurityAuditWriter writer,
        ILogger<ApiSecurityAuditMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ApiEndpointSecurityAuditMetadata? metadata =
            context.GetEndpoint()?.Metadata.GetMetadata<ApiEndpointSecurityAuditMetadata>();
        if (metadata == null)
        {
            await _next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        Write(context, metadata, "started", null, null);
        try
        {
            await _next(context);
            WriteCompletionBestEffort(
                context,
                metadata,
                context.Response.StatusCode < StatusCodes.Status400BadRequest ? "completed" : "rejected",
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch
        {
            WriteCompletionBestEffort(
                context,
                metadata,
                "failed",
                StatusCodes.Status500InternalServerError,
                Stopwatch.GetElapsedTime(startedAt));
            throw;
        }
    }

    private void WriteCompletionBestEffort(
        HttpContext context,
        ApiEndpointSecurityAuditMetadata metadata,
        string phase,
        int statusCode,
        TimeSpan elapsed)
    {
        try
        {
            Write(context, metadata, phase, statusCode, (long)elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "危险维护操作已执行，但完成审计写入失败。CorrelationId={CorrelationId}",
                context.TraceIdentifier);
        }
    }

    private void Write(
        HttpContext context,
        ApiEndpointSecurityAuditMetadata metadata,
        string phase,
        int? statusCode,
        long? elapsedMilliseconds)
    {
        var user = ApiCurrentUserResolver.ResolveCachedUser(context);
        string route = context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? string.Empty
            : string.Empty;
        _writer.Write(new ApiSecurityAuditRecord(
            DateTimeOffset.UtcNow,
            metadata.Category,
            phase,
            context.Request.Method,
            route,
            user?.Id,
            Limit(user?.Username, 128),
            Limit(context.Connection.RemoteIpAddress?.ToString(), 128),
            Limit(context.TraceIdentifier, 96),
            statusCode,
            elapsedMilliseconds));
    }

    private static string Limit(string? value, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}

internal static class ApiSecurityAuditApplicationBuilderExtensions
{
    public static IApplicationBuilder UseExportDocManagerSecurityAudit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ApiSecurityAuditMiddleware>();
    }
}
