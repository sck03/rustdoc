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
