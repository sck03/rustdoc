using System.Text;
using System.Text.Json;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting;

internal static class ApiEndpointPublication
{
    private static readonly Encoding Utf8WithoutBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void Publish(string endpointFile, IEnumerable<string> addresses)
    {
        if (string.IsNullOrWhiteSpace(endpointFile))
        {
            return;
        }

        string apiBaseUrl = ResolveApiBaseUrl(addresses);
        string directory = Path.GetDirectoryName(endpointFile)
            ?? throw new InvalidOperationException("动态端点文件缺少父目录。");
        Directory.CreateDirectory(directory);
        RuntimeFilePermissionHelper.RestrictDirectory(directory);
        string json = JsonSerializer.Serialize(
            new ApiEndpointPublicationRecord(1, apiBaseUrl, Environment.ProcessId),
            JsonOptions);
        AtomicFileHelper.WriteAllTextAtomic(endpointFile, json, Utf8WithoutBom);
        RuntimeFilePermissionHelper.RestrictFile(endpointFile);
    }

    public static void Remove(string endpointFile)
    {
        if (!string.IsNullOrWhiteSpace(endpointFile))
        {
            AtomicFileHelper.TryDeleteFile(endpointFile);
        }
    }

    internal static string ResolveApiBaseUrl(IEnumerable<string> addresses)
    {
        string[] resolved = (addresses ?? Array.Empty<string>())
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out Uri uri) ? uri : null)
            .Where(uri => uri != null &&
                          uri.Scheme == Uri.UriSchemeHttp &&
                          ApiStartupValidator.IsLoopbackHost(uri.Host) &&
                          uri.Port > 0)
            .Select(uri => uri.GetLeftPart(UriPartial.Authority).TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return resolved.Length == 1
            ? resolved[0]
            : throw new InvalidOperationException(
                "API sidecar 启动后没有得到唯一的本机 HTTP 监听地址。");
    }

    private sealed record ApiEndpointPublicationRecord(
        int SchemaVersion,
        string ApiBaseUrl,
        int ProcessId);
}
