using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiExceptionHandlingMiddleware
    {
        public const string CorrelationIdHeaderName = "X-Correlation-ID";
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiExceptionHandlingMiddleware> _logger;

        public ApiExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ApiExceptionHandlingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string correlationId = NormalizeCorrelationId(
                context.Request.Headers[CorrelationIdHeaderName].ToString());
            context.TraceIdentifier = correlationId;
            context.Response.Headers[CorrelationIdHeaderName] = correlationId;

            using (ApiServiceExceptionMapper.PushCorrelationId(correlationId))
            {
                try
                {
                    await _next(context);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogInformation("请求已由客户端取消。CorrelationId={CorrelationId}", correlationId);
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = 499;
                    }
                }
                catch (PayloadLimitExceededException ex)
                {
                    await WriteErrorAsync(
                        context,
                        new ApiServiceError(
                            StatusCodes.Status413PayloadTooLarge,
                            "payload_too_large",
                            ex.Message,
                            correlationId));
                }
                catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    await WriteErrorAsync(
                        context,
                        new ApiServiceError(
                            StatusCodes.Status413PayloadTooLarge,
                            "payload_too_large",
                            "上传内容超过服务器允许的最大大小。",
                            correlationId));
                }
                catch (BadHttpRequestException ex)
                {
                    int statusCode = ex.StatusCode is >= 400 and <= 499
                        ? ex.StatusCode
                        : StatusCodes.Status400BadRequest;
                    await WriteErrorAsync(
                        context,
                        new ApiServiceError(
                            statusCode,
                            "invalid_request",
                            "请求格式无效，请检查提交的数据后重试。",
                            correlationId));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "API 请求发生未处理异常。CorrelationId={CorrelationId}", correlationId);
                    await WriteErrorAsync(context, ApiServiceExceptionMapper.Map(ex, correlationId));
                }
            }
        }

        private static async Task WriteErrorAsync(
            HttpContext context,
            ApiServiceError error)
        {
            if (context.Response.HasStarted)
            {
                throw new InvalidOperationException(
                    $"响应已开始，无法写入错误结果。CorrelationId={error.CorrelationId}");
            }

            context.Response.Clear();
            context.Response.StatusCode = error.StatusCode;
            context.Response.Headers[CorrelationIdHeaderName] = error.CorrelationId;
            await context.Response.WriteAsJsonAsync(
                error.ToResponse(),
                cancellationToken: context.RequestAborted);
        }

        private static string NormalizeCorrelationId(string value)
        {
            string candidate = (value ?? string.Empty).Trim();
            return candidate.Length is > 0 and <= 96 &&
                   candidate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
                ? candidate
                : Guid.NewGuid().ToString("N");
        }
    }

    public sealed class ApiSecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _contentSecurityPolicy;

        public ApiSecurityHeadersMiddleware(RequestDelegate next, ApiRuntimeOptions runtimeOptions)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _contentSecurityPolicy = BuildContentSecurityPolicy(runtimeOptions);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
                headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
                headers.TryAdd(
                    "Content-Security-Policy",
                    _contentSecurityPolicy);
                if (context.Request.IsHttps)
                {
                    headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }

        internal static string BuildContentSecurityPolicy(ApiRuntimeOptions runtimeOptions)
        {
            ArgumentNullException.ThrowIfNull(runtimeOptions);
            string connectSources = string.Join(
                ' ',
                new[] { "'self'" }.Concat(runtimeOptions.AllowedOrigins));
            return "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; " +
                   "img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
                   $"script-src 'self'; connect-src {connectSources}; frame-src 'self' blob:; worker-src 'self' blob:";
        }
    }

    public static class ApiSafetyApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseExportDocManagerApiSafety(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.UseMiddleware<ApiExceptionHandlingMiddleware>();
            app.UseMiddleware<ApiSecurityHeadersMiddleware>();
            return app;
        }
    }
}
