using ExportDocManager.Api.Hosting;
using ExportDocManager.Services.Errors;
using Microsoft.AspNetCore.Http;

namespace ExportDocManager.Api.Tests;

public sealed class ApiServiceExceptionMapperTests
{
    [Theory]
    [MemberData(nameof(ClassifiedExceptions))]
    public void Map_ShouldPreserveServiceErrorCategory(Exception exception, int expectedStatus)
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(exception, "correlation-test");

        Assert.Equal(expectedStatus, status);
        Assert.Equal(
            exception is InfrastructureServiceException
                ? "依赖服务暂时不可用，请稍后重试。"
                : exception.Message,
            message);
    }

    [Fact]
    public void Map_ShouldTreatWrappedInfrastructureFailureAsUnavailableInsteadOfConflict()
    {
        var exception = new InvalidOperationException(
            "保存客户失败。",
            new IOException("database connection was interrupted"));

        (int status, string message) = ApiServiceExceptionMapper.Map(exception, "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
    }

    [Fact]
    public void Map_ShouldTreatInfrastructureWrappedByServiceCategoryAsUnavailable()
    {
        var exception = new ResourceConflictException(
            "业务状态暂时不可用。",
            new IOException("database connection was interrupted"));

        (int status, string message) = ApiServiceExceptionMapper.Map(exception, "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
    }

    [Fact]
    public void Map_ShouldTreatInternalTimeoutCancellationAsUnavailable()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new OperationCanceledException("dependency timeout"),
            "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
    }

    [Fact]
    public void Map_ShouldPreserveExplicitServiceTimeoutWhenInnerExceptionIsInfrastructureTimeout()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new ServiceTimeoutException("renderer timed out", new TimeoutException("process wait")),
            "correlation-test");

        Assert.Equal(StatusCodes.Status504GatewayTimeout, status);
        Assert.Equal("renderer timed out", message);
    }

    [Fact]
    public void Map_ShouldTreatUnclassifiedMissingFileAsInfrastructureFailure()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new FileNotFoundException("missing file"),
            "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
        Assert.DoesNotContain("missing file", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_ShouldTreatAggregateInfrastructureFailureAsUnavailable()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new AggregateException(new InvalidOperationException("operation", new IOException("offline"))),
            "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
    }

    [Fact]
    public void Map_ShouldTreatUnclassifiedUnauthorizedAccessAsInfrastructureAclFailure()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new UnauthorizedAccessException("sensitive path details"),
            "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Contains("权限", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_ShouldPreserveInfrastructureClassificationForWrappedMissingFile()
    {
        (int status, string message) = ApiServiceExceptionMapper.Map(
            new InfrastructureServiceException("file dependency failed", new FileNotFoundException("managed file missing")),
            "correlation-test");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("依赖服务暂时不可用，请稍后重试。", message);
        Assert.DoesNotContain("file dependency", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(UnclassifiedInternalFailures))]
    public void Map_ShouldHideUnclassifiedInternalFailure(Exception exception, string correlationId)
    {
        ApiServiceError error = ApiServiceExceptionMapper.Map(exception, correlationId);

        Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
        Assert.Equal("internal_error", error.Code);
        Assert.Equal(correlationId, error.CorrelationId);
        Assert.DoesNotContain("sensitive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Exception, string> UnclassifiedInternalFailures => new()
    {
        { new Exception("sensitive internal details"), "correlation-test" },
        { new InvalidOperationException("sensitive internal state"), "correlation-invalid-operation" }
    };

    [Fact]
    public void Map_ShouldPreserveExplicitlyUserVisibleInfrastructureMessage()
    {
        ApiServiceError error = ApiServiceExceptionMapper.Map(
            new UserVisibleInfrastructureException("请安装可选模块。"),
            "correlation-visible");

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);
        Assert.Equal("infrastructure_unavailable", error.Code);
        Assert.Equal("请安装可选模块。", error.Message);
    }

    public static TheoryData<Exception, int> ClassifiedExceptions => new()
    {
        { new ServiceValidationException("validation"), StatusCodes.Status400BadRequest },
        { new ResourceNotFoundException("missing"), StatusCodes.Status404NotFound },
        { new PermissionDeniedException("forbidden"), StatusCodes.Status403Forbidden },
        { new ServiceBusyException("busy"), StatusCodes.Status429TooManyRequests },
        { new ServiceTimeoutException("timeout"), StatusCodes.Status504GatewayTimeout },
        { new ResourceConflictException("conflict"), StatusCodes.Status409Conflict },
        { new ServiceConcurrencyException("concurrency"), StatusCodes.Status409Conflict },
        { new InfrastructureServiceException("unavailable"), StatusCodes.Status503ServiceUnavailable },
        { new InsufficientStorageException("disk full"), StatusCodes.Status507InsufficientStorage }
    };
}
