using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Tools
{
    public class OpenAiCompatibleService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;

        public OpenAiCompatibleService(HttpClient httpClient, ISettingsService settingsService)
        {
            _httpClient = httpClient;
            _settingsService = settingsService;
        }

        public async Task<string> AnalyzeComplianceAsync(string prompt, string content, CancellationToken cancellationToken = default)
        {
            var aiSettings = _settingsService.Settings?.AI ?? new AISettings();
            var endpoint = BuildEndpointUri(string.IsNullOrWhiteSpace(aiSettings.ApiEndpoint)
                ? "https://api.deepseek.com/v1/chat/completions"
                : aiSettings.ApiEndpoint.Trim());
            var model = string.IsNullOrWhiteSpace(aiSettings.ModelName)
                ? "deepseek-chat"
                : aiSettings.ModelName.Trim();
            var apiKey = aiSettings.ApiKey?.Trim() ?? string.Empty;
            
            // Allow empty API Key only for loopback endpoints (like Ollama on localhost).
            if (string.IsNullOrWhiteSpace(apiKey) && !endpoint.IsLoopback)
            {
                throw new InvalidOperationException("AI API Key 未配置，请先到“系统设置 > AI 审查配置”中填写。");
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
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"AI request failed with status {response.StatusCode}: {ExtractErrorMessage(errorBody)}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
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

            throw new InvalidOperationException("Failed to parse AI response.");
        }

        private static Uri BuildEndpointUri(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("AI 接口地址无效，请在“系统设置 > AI 审查配置”中填写完整的 http/https 地址。");
            }

            return uri;
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
    }
}
