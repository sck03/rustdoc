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
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Infrastructure
{
    public class WebDavCloudSyncService : ICloudSyncService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<WebDavCloudSyncService> _logger;

        public WebDavCloudSyncService(ISettingsService settingsService, ILogger<WebDavCloudSyncService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        private WebDavSettings Config => _settingsService.Settings?.WebDav ?? new WebDavSettings();

        public async Task UploadFileAsync(string localFilePath, string remoteFileName)
        {
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);

            if (!File.Exists(localFilePath))
                throw new FileNotFoundException("Local file not found", localFilePath);

            string normalizedRemoteFileName = NormalizeRemoteFileName(remoteFileName);
            string encodedFileName = Uri.EscapeDataString(normalizedRemoteFileName);
            var uploadUri = BuildUri($"{baseUrl}/{encodedFileName}");

            using var client = CreateClient(config, userName, TimeSpan.FromMinutes(10));
            using var content = new StreamContent(File.OpenRead(localFilePath));
            using var response = await client.PutAsync(uploadUri, content);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Upload failed: {response.StatusCode} - {error}");
            }

            _logger.LogInformation("Successfully uploaded {LocalFilePath} to {UploadUri}", localFilePath, uploadUri);
        }

        public async Task<IReadOnlyList<CloudBackupFileInfo>> ListBackupFilesAsync()
        {
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);
            var url = BuildUri(baseUrl);

            using var client = CreateClient(config, userName, TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
            request.Headers.TryAddWithoutValidation("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:getcontentlength/><d:getlastmodified/><d:resourcetype/></d:prop></d:propfind>",
                Encoding.UTF8,
                "application/xml");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"List failed: {response.StatusCode} - {error}");
            }

            string xml = await response.Content.ReadAsStringAsync();
            return ParsePropFindBackupFiles(xml);
        }

        public async Task DownloadFileAsync(string remoteFileName, string localFilePath)
        {
            var config = Config;
            string baseUrl = NormalizeConfiguredBaseUrl(config, out string userName);
            string normalizedRemoteFileName = NormalizeRemoteFileName(remoteFileName);
            var downloadUri = BuildUri($"{baseUrl}/{Uri.EscapeDataString(normalizedRemoteFileName)}");

            using var client = CreateClient(config, userName, TimeSpan.FromMinutes(10));
            using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Download failed: {response.StatusCode} - {error}");
            }

            await AtomicFileHelper.WriteFileAtomicAsync(
                localFilePath,
                async (tempPath, cancellationToken) =>
                {
                    await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var targetStream = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await sourceStream.CopyToAsync(targetStream, cancellationToken);
                });

            _logger.LogInformation("Successfully downloaded {RemoteFileName} from WebDAV to {LocalFilePath}", normalizedRemoteFileName, localFilePath);
        }

        public async Task<bool> TestConnectionAsync(WebDavSettings settings)
        {
            settings ??= new WebDavSettings();
            string baseUrl = NormalizeBaseUrl(settings.Url);
            string userName = settings.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(userName))
                return false;

            Uri url;
            try
            {
                url = BuildUri(baseUrl);
            }
            catch
            {
                return false;
            }

            try
            {
                using var client = CreateClient(settings, userName, TimeSpan.FromSeconds(15));

                using var request = new HttpRequestMessage(HttpMethod.Options, url);
                using var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                using var propfindRequest = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
                propfindRequest.Headers.TryAddWithoutValidation("Depth", "0");
                using var propfindResponse = await client.SendAsync(propfindRequest);
                return propfindResponse.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebDAV connection test failed.");
                return false;
            }
        }

        private static string NormalizeBaseUrl(string url)
        {
            return string.IsNullOrWhiteSpace(url)
                ? string.Empty
                : url.Trim().TrimEnd('/');
        }

        private static string NormalizeConfiguredBaseUrl(WebDavSettings config, out string userName)
        {
            config ??= new WebDavSettings();
            string baseUrl = NormalizeBaseUrl(config.Url);
            userName = config.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("WebDAV settings are not configured.");

            return baseUrl;
        }

        private static HttpClient CreateClient(WebDavSettings config, string userName, TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(userName, config.Password ?? string.Empty)
            };

            return new HttpClient(handler)
            {
                Timeout = timeout
            };
        }

        private static string NormalizeRemoteFileName(string remoteFileName)
        {
            string fileName = (remoteFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Remote file name cannot be empty.", nameof(remoteFileName));
            }

            if (fileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0 ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            {
                throw new ArgumentException("Remote file name cannot contain a path.", nameof(remoteFileName));
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
                .Where(file => file != null)
                .Select(file => file)
                .Where(file => file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastModified)
                .ThenByDescending(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static CloudBackupFileInfo ParsePropFindBackupFile(XElement response, XNamespace dav)
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
            DateTime lastModified = DateTime.MinValue;
            string modifiedText = prop.Element(dav + "getlastmodified")?.Value ?? string.Empty;
            if (DateTimeOffset.TryParse(modifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedModified))
            {
                lastModified = parsedModified.UtcDateTime;
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
                throw new InvalidOperationException($"Invalid WebDAV url: {url}");
            }

            return uri;
        }
    }
}
