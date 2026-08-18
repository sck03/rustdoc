using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Tools
{
    public class OpenAiCompatibleService : IAIService
    {
        private const long MaximumResponseBytes = 4L * 1024L * 1024L;
        private const long MaximumErrorResponseBytes = 64L * 1024L;
        private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(2);
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;

        public OpenAiCompatibleService(HttpClient httpClient, ISettingsService settingsService)
        {
            _httpClient = httpClient;
            _settingsService = settingsService;
        }

        public async Task<string> AnalyzeComplianceAsync(string prompt, string content, CancellationToken cancellationToken = default)
        {
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationCancellation.CancelAfter(OperationTimeout);
            CancellationToken operationToken = operationCancellation.Token;
            var aiSettings = _settingsService.Settings?.AI ?? new AISettings();
            var endpoint = AiEndpointPolicy.Normalize(aiSettings.ApiEndpoint);
            var model = string.IsNullOrWhiteSpace(aiSettings.ModelName)
                ? "deepseek-chat"
                : aiSettings.ModelName.Trim();
            var apiKey = aiSettings.ApiKey?.Trim() ?? string.Empty;

            // Allow empty API Key only for loopback endpoints (like Ollama on localhost).
            if (string.IsNullOrWhiteSpace(apiKey) && !endpoint.IsLoopback)
            {
                throw new ServiceValidationException("AI API Key 未配置，请先到“系统设置 > AI 审查配置”中填写。");
            }

            var systemPrompt = string.IsNullOrWhiteSpace(aiSettings.SystemPrompt)
                ? "你是一个专业的国际贸易信用证(L/C)单证审核专家。你的任务是：\n1. 仔细核对信用证条款与实际发票/装箱单数据。\n2. 严格遵循 UCP600 惯例。\n3. 找出所有不符点 (Discrepancies) 并提供修改建议。\n请以清晰、专业的结构输出审查报告。"
                : aiSettings.SystemPrompt;

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt + "\n\n" + content }
                },
                temperature = 0.3
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = requestContent
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await ReadBoundedTextAsync(
                    response.Content,
                    MaximumErrorResponseBytes,
                    operationToken);
                throw new HttpRequestException($"AI request failed with status {response.StatusCode}: {ExtractErrorMessage(errorBody)}");
            }

            var responseBody = await ReadBoundedTextAsync(response.Content, MaximumResponseBytes, operationToken);
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var text))
                {
                    return ExtractResponseText(text);
                }
            }

            throw new InfrastructureServiceException("AI 服务返回了无法解析的响应。");
        }

        private static string ExtractErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return "无错误详情";
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                    {
                        return error.GetString() ?? responseBody;
                    }

                    if (error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var message) &&
                        message.ValueKind == JsonValueKind.String)
                    {
                        return message.GetString() ?? responseBody;
                    }
                }
            }
            catch
            {
            }

            return responseBody;
        }

        private static string ExtractResponseText(JsonElement content)
        {
            return content.ValueKind switch
            {
                JsonValueKind.String => content.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(
                    Environment.NewLine,
                    content.EnumerateArray()
                        .Select(ExtractTextPart)
                        .Where(part => !string.IsNullOrWhiteSpace(part))),
                _ => string.Empty
            };
        }

        private static string ExtractTextPart(JsonElement part)
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                return part.GetString() ?? string.Empty;
            }

            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static async Task<string> ReadBoundedTextAsync(
            HttpContent content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
            {
                throw new PayloadLimitExceededException(maximumBytes);
            }

            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            return await BoundedStreamHelper.ReadUtf8TextAsync(stream, maximumBytes, cancellationToken);
        }
    }
}
