using System.ComponentModel;
using System.Data.Common;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

internal static class ApiServiceExceptionMapper
{
    private static readonly AsyncLocal<string> CurrentCorrelationId = new();

    internal static IDisposable PushCorrelationId(string correlationId)
    {
        string previous = CurrentCorrelationId.Value;
        CurrentCorrelationId.Value = correlationId;
        return new CorrelationScope(previous);
    }

    public static IResult ToResult(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        (int statusCode, string message) = Map(exception, CurrentCorrelationId.Value);
        return Results.Json(new ApiErrorResponse(message), statusCode: statusCode);
    }

    public static (int StatusCode, string Message) Map(Exception exception, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ServiceException explicitLoadSignal = Enumerate(exception)
            .OfType<ServiceException>()
            .FirstOrDefault(current => current is ServiceBusyException or ServiceTimeoutException);
        if (explicitLoadSignal is ServiceBusyException busySignal)
        {
            return (StatusCodes.Status429TooManyRequests, busySignal.Message);
        }
        if (explicitLoadSignal is ServiceTimeoutException timeoutSignal)
        {
            return (StatusCodes.Status504GatewayTimeout, timeoutSignal.Message);
        }

        if (ContainsNotFound(exception))
        {
            return (StatusCodes.Status404NotFound, FindNotFoundMessage(exception));
        }

        if (Enumerate(exception).Any(current => current is UnauthorizedAccessException))
        {
            return (
                StatusCodes.Status503ServiceUnavailable,
                "运行目录或依赖资源暂时不可访问，请联系管理员检查权限。");
        }

        // An infrastructure failure can be wrapped by an outer service/operation
        // exception. Preserve the transport contract (503) instead of allowing
        // the outer InvalidOperation/Conflict category to misreport a database,
        // filesystem, timeout, or HTTP dependency outage as a business conflict.
        if (ContainsInfrastructureFailure(exception))
        {
            return (StatusCodes.Status503ServiceUnavailable, "依赖服务暂时不可用，请稍后重试。");
        }

        // Preserve a classified service error even when an adapter adds an
        // generic wrapper or an AggregateException. This keeps the transport
        // contract independent from the async/hosting boundary.
        ServiceException nestedServiceException = Enumerate(exception)
            .Skip(1)
            .OfType<ServiceException>()
            .FirstOrDefault();
        if (nestedServiceException != null)
        {
            return Map(nestedServiceException, correlationId);
        }

        return exception switch
        {
            PayloadLimitExceededException payload =>
                (StatusCodes.Status413PayloadTooLarge, payload.Message),
            ServiceValidationException validation =>
                (StatusCodes.Status400BadRequest, validation.Message),
            ResourceNotFoundException notFound =>
                (StatusCodes.Status404NotFound, notFound.Message),
            PermissionDeniedException permission =>
                (StatusCodes.Status403Forbidden, permission.Message),
            InsufficientStorageException storage =>
                (StatusCodes.Status507InsufficientStorage, storage.Message),
            ServiceBusyException busy =>
                (StatusCodes.Status429TooManyRequests, busy.Message),
            ServiceTimeoutException timeout =>
                (StatusCodes.Status504GatewayTimeout, timeout.Message),
            ServiceConcurrencyException concurrency =>
                (StatusCodes.Status409Conflict, concurrency.Message),
            ResourceConflictException conflict =>
                (StatusCodes.Status409Conflict, conflict.Message),
            InfrastructureServiceException infrastructure =>
                (StatusCodes.Status503ServiceUnavailable, infrastructure.Message),
            UnauthorizedAccessException =>
                (StatusCodes.Status503ServiceUnavailable, "运行目录或依赖资源暂时不可访问，请联系管理员检查权限。"),
            FileNotFoundException or DirectoryNotFoundException =>
                (StatusCodes.Status404NotFound, exception.Message),
            KeyNotFoundException missing =>
                (StatusCodes.Status404NotFound, missing.Message),
            ArgumentException or FormatException or InvalidDataException or NotSupportedException =>
                (StatusCodes.Status400BadRequest, exception.Message),
            IOException or DbException or TimeoutException or HttpRequestException =>
                (StatusCodes.Status503ServiceUnavailable, "依赖服务暂时不可用，请稍后重试。"),
            // Internal operation timeouts use cancellation tokens as well, but
            // are not client disconnects (those are handled by the middleware
            // first). They are dependency availability failures, not conflicts.
            OperationCanceledException =>
                (StatusCodes.Status503ServiceUnavailable, "依赖服务暂时不可用，请稍后重试。"),
            _ =>
                (StatusCodes.Status500InternalServerError,
                    string.IsNullOrWhiteSpace(correlationId)
                        ? "服务器处理请求时发生错误，请稍后重试。"
                        : $"服务器处理请求时发生错误，请联系管理员并提供关联编号 {correlationId}。")
        };
    }

    private static bool ContainsInfrastructureFailure(Exception exception)
    {
        foreach (Exception current in Enumerate(exception))
        {
            if (current is DbException or TimeoutException or HttpRequestException or
                SocketException or Win32Exception or UnauthorizedAccessException or
                (IOException and not FileNotFoundException and not DirectoryNotFoundException))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsNotFound(Exception exception)
    {
        foreach (Exception current in Enumerate(exception))
        {
            if (current is FileNotFoundException or DirectoryNotFoundException or
                ResourceNotFoundException or KeyNotFoundException)
            {
                return true;
            }
        }
        return false;
    }

    private static string FindNotFoundMessage(Exception exception)
    {
        foreach (Exception current in Enumerate(exception))
        {
            if (current is ResourceNotFoundException or FileNotFoundException or
                DirectoryNotFoundException or KeyNotFoundException)
            {
                return current.Message;
            }
        }
        return "未找到请求的资源。";
    }

    private static IEnumerable<Exception> Enumerate(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.Count > 0)
        {
            Exception current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is AggregateException aggregate)
            {
                for (int index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
            }
        }
    }

    private sealed class CorrelationScope : IDisposable
    {
        private readonly string _previous;
        private bool _disposed;

        public CorrelationScope(string previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentCorrelationId.Value = _previous;
        }
    }
}
