using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ExportDocManager.Models;
using ExportDocManager.Services.Errors;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Infrastructure
{
    public class WebDavCloudSyncService : ICloudSyncService
    {
        public const long MaximumUploadBytes = 4L * 1024L * 1024L * 1024L;
        public const long MaximumDownloadBytes = 4L * 1024L * 1024L * 1024L;
        private const int TransferBufferBytes = 128 * 1024;
        private const long MaximumPropFindResponseBytes = 2L * 1024L * 1024L;
        private const long MaximumErrorResponseBytes = 64L * 1024L;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<WebDavCloudSyncService> _logger;

        public WebDavCloudSyncService(ISettingsService settingsService, ILogger<WebDavCloudSyncService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        private WebDavSettings Config => _settingsService.Settings?.WebDav ?? new WebDavSettings();

        public async Task UploadFileAsync(
            string localFilePath,
            string remoteFileName,
            CancellationToken cancellationToken = default)
        {
            using var operationCancellation = CreateOperationCancellation(
                TimeSpan.FromMinutes(10),
                cancellationToken);
            CancellationToken operationToken = operationCancellation.Token;
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);

            var localFile = new FileInfo(localFilePath);
            if (!localFile.Exists)
                throw new ResourceNotFoundException("找不到要上传的本地备份文件。");
            if (localFile.Length <= 0)
                throw new ServiceValidationException("要上传的本地备份文件为空。");
            if (localFile.Length > MaximumUploadBytes)
                throw new PayloadLimitExceededException(MaximumUploadBytes);

            string normalizedRemoteFileName = NormalizeRemoteFileName(remoteFileName);
            string encodedFileName = Uri.EscapeDataString(normalizedRemoteFileName);
            var uploadUri = BuildUri($"{baseUrl}/{encodedFileName}");

            using var client = CreateClient(config, userName, TimeSpan.FromMinutes(10));
            await using var sourceStream = new FileStream(
                localFile.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                TransferBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri)
            {
                Content = new StreamContent(sourceStream, TransferBufferBytes)
            };
            request.Content.Headers.ContentLength = localFile.Length;
            using var response = await SendWithoutRedirectAsync(
                client,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = await ReadBoundedTextAsync(response.Content, MaximumErrorResponseBytes, operationToken);
                throw new HttpRequestException($"Upload failed: {response.StatusCode} - {error}");
            }

            _logger.LogInformation("Successfully uploaded {LocalFilePath} to {UploadUri}", localFilePath, uploadUri);
        }

        public async Task<IReadOnlyList<CloudBackupFileInfo>> ListBackupFilesAsync(
            CancellationToken cancellationToken = default)
        {
            using var operationCancellation = CreateOperationCancellation(
                TimeSpan.FromSeconds(30),
                cancellationToken);
            CancellationToken operationToken = operationCancellation.Token;
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);
            var url = BuildUri(baseUrl + "/");

            using var client = CreateClient(config, userName, TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>",
                Encoding.UTF8,
                "application/xml");

            using var response = await SendWithoutRedirectAsync(
                client,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken);
            if (!response.IsSuccessStatusCode)
            {
                string error = await ReadBoundedTextAsync(response.Content, MaximumErrorResponseBytes, operationToken);
                throw new HttpRequestException($"List failed: {response.StatusCode} - {error}");
            }

            string xml = await ReadBoundedTextAsync(response.Content, MaximumPropFindResponseBytes, operationToken);
            return ParsePropFindBackupFiles(xml);
        }

        public async Task DownloadFileAsync(
            string remoteFileName,
            string localFilePath,
            CancellationToken cancellationToken = default)
        {
            using var operationCancellation = CreateOperationCancellation(
                TimeSpan.FromMinutes(10),
                cancellationToken);
            CancellationToken operationToken = operationCancellation.Token;
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);
            string normalizedRemoteFileName = NormalizeRemoteFileName(remoteFileName);
            var downloadUri = BuildUri($"{baseUrl}/{Uri.EscapeDataString(normalizedRemoteFileName)}");

            using var client = CreateClient(config, userName, TimeSpan.FromMinutes(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            using var response = await SendWithoutRedirectAsync(
                client,
                request,
                HttpCompletionOption.ResponseHeadersRead,
                operationToken);
            if (!response.IsSuccessStatusCode)
            {
                string error = await ReadBoundedTextAsync(response.Content, MaximumErrorResponseBytes, operationToken);
                throw new HttpRequestException($"Download failed: {response.StatusCode} - {error}");
            }

            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            {
                throw new PayloadLimitExceededException(MaximumDownloadBytes);
            }

            await AtomicFileHelper.WriteFileAtomicAsync(
                localFilePath,
                async (tempPath, token) =>
                {
                    await using var sourceStream = await response.Content.ReadAsStreamAsync(token);
                    await using var targetStream = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await BoundedStreamHelper.CopyToAsync(
                        sourceStream,
                        targetStream,
                        MaximumDownloadBytes,
                        token);
                },
                operationToken);

            _logger.LogInformation("Successfully downloaded {RemoteFileName} from WebDAV to {LocalFilePath}", normalizedRemoteFileName, localFilePath);
        }

        public async Task<bool> TestConnectionAsync(
            WebDavSettings settings,
            CancellationToken cancellationToken = default)
        {
            using var operationCancellation = CreateOperationCancellation(
                TimeSpan.FromSeconds(15),
                cancellationToken);
            CancellationToken operationToken = operationCancellation.Token;
            settings ??= new WebDavSettings();
            bool urlValid = WebDavEndpointPolicy.TryNormalize(settings.Url, out string baseUrl, out _);
            string userName = settings.UserName?.Trim() ?? string.Empty;
            if (!urlValid || string.IsNullOrWhiteSpace(userName))
                return false;

            Uri url;
            try
            {
                url = BuildUri(baseUrl + "/");
            }
            catch
            {
                return false;
            }

            try
            {
                using var client = CreateClient(settings, userName, TimeSpan.FromSeconds(15));

                using var request = new HttpRequestMessage(HttpMethod.Options, url);
                using var response = await SendWithoutRedirectAsync(
                    client,
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    operationToken);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                using var propfindRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                propfindRequest.Headers.TryAddWithoutValidation("Depth", "0");
                using var propfindResponse = await SendWithoutRedirectAsync(
                    client,
                    propfindRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    operationToken);
                return propfindResponse.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebDAV connection test failed.");
                return false;
            }
        }

        private static string NormalizeConfiguredBaseUrl(WebDavSettings config, out string userName)
        {
            config ??= new WebDavSettings();
            if (!WebDavEndpointPolicy.TryNormalize(config.Url, out string baseUrl, out string errorMessage))
            {
                throw new ServiceValidationException(errorMessage);
            }
            userName = config.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userName))
                throw new ServiceValidationException("WebDAV 尚未配置，请先保存服务器地址和用户名。");

            return baseUrl;
        }

        private static HttpClient CreateClient(WebDavSettings config, string userName, TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                Credentials = new NetworkCredential(userName, config.Password ?? string.Empty)
            };

            return new HttpClient(handler)
            {
                Timeout = timeout
            };
        }

        internal static async Task<HttpResponseMessage> SendWithoutRedirectAsync(
            HttpClient client,
            HttpRequestMessage request,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);

            HttpResponseMessage response = await client
                .SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw new ServiceValidationException(
                    "WebDAV 服务返回了重定向。为防止账号凭据或大文件被转发，请把地址改为最终的 HTTPS WebDAV 目录。");
            }

            return response;
        }

        private static string NormalizeRemoteFileName(string remoteFileName)
        {
            string fileName = (remoteFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ServiceValidationException("远端备份文件名不能为空。", new ArgumentException("Remote file name cannot be empty.", nameof(remoteFileName)));
            }

            if (fileName.Contains('/') ||
                fileName.Contains('\\') ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new ServiceValidationException("远端备份文件名不能包含路径。", new ArgumentException("Remote file name cannot contain a path.", nameof(remoteFileName)));
            }

            return fileName;
        }

        private static IReadOnlyList<CloudBackupFileInfo> ParsePropFindBackupFiles(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return Array.Empty<CloudBackupFileInfo>();
            }

            var document = XDocument.Parse(xml);
            XNamespace dav = "DAV:";
            return document
                .Descendants(dav + "response")
                .Select(response => ParsePropFindBackupFile(response, dav))
                .OfType<CloudBackupFileInfo>()
                .Where(file => file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastModified)
                .ThenByDescending(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static CloudBackupFileInfo? ParsePropFindBackupFile(XElement response, XNamespace dav)
        {
            var prop = response
                .Elements(dav + "propstat")
                .Elements(dav + "prop")
                .FirstOrDefault();
            if (prop == null || prop.Element(dav + "resourcetype")?.Element(dav + "collection") != null)
            {
                return null;
            }

            string displayName = prop.Element(dav + "displayname")?.Value?.Trim() ?? string.Empty;
            string href = response.Element(dav + "href")?.Value?.Trim() ?? string.Empty;
            string fileName = !string.IsNullOrWhiteSpace(displayName) ? displayName : ReadFileNameFromHref(href);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            _ = long.TryParse(prop.Element(dav + "getcontentlength")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long sizeBytes);
            DateTimeOffset lastModified = DateTimeOffset.MinValue;
            string modifiedText = prop.Element(dav + "getlastmodified")?.Value ?? string.Empty;
            if (DateTimeOffset.TryParse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedModified))
            {
                lastModified = parsedModified.ToUniversalTime();
            }

            return new CloudBackupFileInfo(fileName, Math.Max(sizeBytes, 0), lastModified);
        }

        private static string ReadFileNameFromHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
            {
                return Uri.UnescapeDataString(Path.GetFileName(absoluteUri.LocalPath));
            }

            return Uri.UnescapeDataString(Path.GetFileName(href.TrimEnd('/')));
        }

        private static Uri BuildUri(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ServiceValidationException($"WebDAV 地址无效：{url}");
            }

            return uri;
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

        private static CancellationTokenSource CreateOperationCancellation(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(timeout);
            return source;
        }
    }
}
