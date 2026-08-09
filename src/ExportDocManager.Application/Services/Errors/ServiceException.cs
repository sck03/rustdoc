namespace ExportDocManager.Services.Errors;

/// <summary>
/// 应用服务向 API 或桌面适配器传递的可预期错误基类。
/// 基础设施异常必须保留 InnerException，不能伪装成业务冲突。
/// </summary>
public abstract class ServiceException : Exception
{
    protected ServiceException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class ServiceValidationException : ServiceException
{
    public ServiceValidationException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class ResourceNotFoundException : ServiceException
{
    public ResourceNotFoundException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class ResourceConflictException : ServiceException
{
    public ResourceConflictException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class PermissionDeniedException : ServiceException
{
    public PermissionDeniedException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class ServiceConcurrencyException : ResourceConflictException
{
    public ServiceConcurrencyException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class InfrastructureServiceException : ServiceException
{
    public InfrastructureServiceException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Indicates that an interrupted server migration cannot be reconciled safely
/// without an administrator restoring the retained safety backup. The API must
/// fail closed instead of serving business requests against potentially mixed
/// database and file state.
/// </summary>
public sealed class ManualRecoveryRequiredException : InfrastructureServiceException
{
    public ManualRecoveryRequiredException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class InsufficientStorageException : ServiceException
{
    public InsufficientStorageException(
        string message,
        long requiredBytes = 0,
        long availableBytes = 0,
        Exception innerException = null)
        : base(message, innerException)
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }

    public long AvailableBytes { get; }
}

public class ServiceBusyException : ServiceException
{
    public ServiceBusyException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}

public class ServiceTimeoutException : ServiceException
{
    public ServiceTimeoutException(string message, Exception innerException = null)
        : base(message, innerException)
    {
    }
}
