using System.ComponentModel;
using System.Data.Common;
using System.Net.Http;
using System.Net.Sockets;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

internal sealed record ApiServiceError(
    int StatusCode,
    string Code,
    string Message,
    string? CorrelationId)
{
    public ApiErrorResponse ToResponse() => new(Message, Code, CorrelationId);

    public void Deconstruct(out int statusCode, out string message)
    {
        statusCode = StatusCode;
        message = Message;
    }
}

internal static class ApiServiceExceptionMapper
{
    private const string UnavailableMessage = "依赖服务暂时不可用，请稍后重试。";
    private static readonly AsyncLocal<string?> CurrentCorrelationId = new();

    internal static IDisposable PushCorrelationId(string correlationId)
    {
        string? previous = CurrentCorrelationId.Value;
        CurrentCorrelationId.Value = correlationId;
        return new CorrelationScope(previous);
    }

    public static IResult ToResult(Exception exception)
    {
        ApiServiceError error = Map(exception, CurrentCorrelationId.Value);
        return Results.Json(error.ToResponse(), statusCode: error.StatusCode);
    }

    public static ApiServiceError Map(Exception exception, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ServiceException? classified = Enumerate(exception).OfType<ServiceException>().FirstOrDefault();
        if (classified != null)
        {
            return classified is ResourceConflictException && ContainsInfrastructureFailure(exception)
                ? Create(503, "infrastructure_unavailable", UnavailableMessage, correlationId)
                : MapServiceException(classified, correlationId);
        }

        if (Enumerate(exception).FirstOrDefault(current =>
                current is ResourceNotFoundException or KeyNotFoundException) is { } missing)
        {
            return Create(404, "not_found", missing.Message, correlationId);
        }

        if (Enumerate(exception).Any(current => current is UnauthorizedAccessException))
        {
            return Create(
                503,
                "infrastructure_unavailable",
                "运行目录或依赖资源暂时不可访问，请联系管理员检查权限。",
                correlationId);
        }

        if (ContainsInfrastructureFailure(exception))
        {
            return Create(503, "infrastructure_unavailable", UnavailableMessage, correlationId);
        }

        return exception switch
        {
            PayloadLimitExceededException payload =>
                Create(413, "payload_too_large", payload.Message, correlationId),
            KeyNotFoundException notFound => Create(404, "not_found", notFound.Message, correlationId),
            ArgumentException or FormatException or InvalidDataException =>
                Create(400, "validation_error", exception.Message, correlationId),
            NotSupportedException =>
                Create(400, "unsupported_request", "当前请求不受支持。", correlationId),
            OperationCanceledException =>
                Create(503, "infrastructure_unavailable", UnavailableMessage, correlationId),
            _ => Create(
                500,
                "internal_error",
                "服务器处理请求时发生错误，请稍后重试。",
                correlationId)
        };
    }

    private static ApiServiceError MapServiceException(
        ServiceException exception,
        string? correlationId) => exception switch
        {
            ServiceValidationException value => Create(400, "validation_error", value.Message, correlationId),
            ResourceNotFoundException value => Create(404, "not_found", value.Message, correlationId),
            PermissionDeniedException value => Create(403, "forbidden", value.Message, correlationId),
            InsufficientStorageException value => Create(507, "insufficient_storage", value.Message, correlationId),
            ServiceBusyException value => Create(429, "service_busy", value.Message, correlationId),
            ServiceTimeoutException value => Create(504, "service_timeout", value.Message, correlationId),
            ServiceConcurrencyException value => Create(409, "conflict", value.Message, correlationId),
            ResourceConflictException value => Create(409, "conflict", value.Message, correlationId),
            UserVisibleInfrastructureException value =>
                Create(503, "infrastructure_unavailable", value.Message, correlationId),
            InfrastructureServiceException =>
                Create(503, "infrastructure_unavailable", UnavailableMessage, correlationId),
            _ => Create(500, "internal_error", "服务器处理请求时发生错误，请稍后重试。", correlationId)
        };

    private static ApiServiceError Create(
        int statusCode,
        string code,
        string message,
        string? correlationId) =>
        new(statusCode, code, message, string.IsNullOrWhiteSpace(correlationId) ? null : correlationId);

    private static bool ContainsInfrastructureFailure(Exception exception) =>
        Enumerate(exception).Any(current => current is
            DbException or TimeoutException or HttpRequestException or SocketException or
            Win32Exception or UnauthorizedAccessException or IOException);

    private static IEnumerable<Exception> Enumerate(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.TryPop(out Exception? current))
        {
            if (!visited.Add(current)) continue;
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions.Reverse()) pending.Push(inner);
            }
            else if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
            }
        }
    }

    private sealed class CorrelationScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentCorrelationId.Value = previous;
        }
    }
}
