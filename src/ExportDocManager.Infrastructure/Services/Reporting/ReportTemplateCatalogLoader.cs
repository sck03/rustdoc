using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportTemplateCatalogLoader
    {
        public const string ExportTemplateCatalogType = "Export";
        public const string InternalTemplateCatalogType = "Internal";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly ReportTemplatePathResolver _pathResolver;

        public ReportTemplateCatalogLoader(ReportTemplatePathResolver pathResolver)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public async Task<IReadOnlyList<ReportTemplateConfig>> LoadResolvedConfigsAsync(
            CancellationToken cancellationToken = default)
        {
            string basePath = _pathResolver.GetTemplatesBaseDirectory();
            string configPath = Path.Combine(basePath, "report_templates.json");
            var configuredRows = new List<ReportTemplateConfig>();

            if (File.Exists(configPath))
            {
                string json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (ValidateReportTemplateConfig(json))
                {
                    var root = JsonSerializer.Deserialize<ReportTemplateConfigRoot>(json, JsonOptions);
                    if (root?.Reports != null)
                    {
                        foreach (var cfg in root.Reports)
                        {
                            if (cfg == null || string.IsNullOrWhiteSpace(cfg.FileName))
                            {
                                continue;
                            }

                            string path = cfg.FileName;
                            if (!Path.IsPathRooted(path))
                            {
                                path = Path.Combine(basePath, path);
                            }

                            configuredRows.Add(new ReportTemplateConfig
                            {
                                Type = NormalizeTemplateCatalogType(cfg.Type, path),
                                FileName = Path.GetFullPath(path),
                                Name = cfg.Name,
                                WithSeal = cfg.WithSeal
                            });
                        }
                    }
                }
                else
                {
                    Log.Warning("报表模板配置文件格式无效");
                }
            }

            return BuildResolvedTemplateConfigs(basePath, configuredRows, cancellationToken);
        }

        public List<ReportTemplateConfig> BuildResolvedTemplateConfigs(
            string basePath,
            IEnumerable<ReportTemplateConfig> configuredRows,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(basePath);
            var baseFullPath = Path.GetFullPath(basePath);
            var resolved = new List<ReportTemplateConfig>();
            var configuredByPath = new Dictionary<string, ReportTemplateConfig>(StringComparer.OrdinalIgnoreCase);
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in configuredRows ?? Enumerable.Empty<ReportTemplateConfig>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.FileName))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(row.FileName);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                configuredByPath[fullPath] = new ReportTemplateConfig
                {
                    Type = NormalizeTemplateCatalogType(row.Type, fullPath),
                    FileName = fullPath,
                    Name = NormalizeTemplateDisplayName(row.Name, fullPath),
                    WithSeal = row.WithSeal ?? true
                };
            }

            foreach (var templatePath in Directory.GetFiles(baseFullPath, "*.html", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!addedPaths.Add(templatePath))
                {
                    continue;
                }

                if (configuredByPath.TryGetValue(templatePath, out var configured))
                {
                    resolved.Add(CloneTemplateConfig(configured));
                    continue;
                }

                resolved.Add(new ReportTemplateConfig
                {
                    Type = NormalizeTemplateCatalogType(null, templatePath),
                    FileName = templatePath,
                    Name = NormalizeTemplateDisplayName(null, templatePath),
                    WithSeal = true
                });
            }

            foreach (var configured in configuredByPath
                         .Where(entry => !ReportTemplatePathResolver.IsPathWithinDirectory(entry.Key, baseFullPath))
                         .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (addedPaths.Add(configured.Key))
                {
                    resolved.Add(CloneTemplateConfig(configured.Value));
                }
            }

            return resolved;
        }

        public Dictionary<ReportDocumentType, string> BuildTemplatePathCache(IEnumerable<ReportTemplateConfig> configs)
        {
            var cache = new Dictionary<ReportDocumentType, string>();

            foreach (var config in configs ?? Enumerable.Empty<ReportTemplateConfig>())
            {
                if (config == null || string.IsNullOrWhiteSpace(config.FileName) || !File.Exists(config.FileName))
                {
                    continue;
                }

                var reportType = ResolveCatalogReportType(config.Type, config.FileName);
                var fullPath = Path.GetFullPath(config.FileName);
                if (!cache.TryGetValue(reportType, out var existingPath) ||
                    IsPreferredDefaultTemplate(fullPath, reportType) ||
                    !IsPreferredDefaultTemplate(existingPath, reportType))
                {
                    cache[reportType] = fullPath;
                }
            }

            return cache;
        }

        public string NormalizeStoredTemplatePath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            var absolutePath = _pathResolver.ToAbsolutePath(templatePath.Trim());
            return _pathResolver.ToStoredPath(absolutePath);
        }

        public string NormalizeAbsoluteTemplatePath(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return string.Empty;
            }

            return Path.GetFullPath(_pathResolver.ToAbsolutePath(templatePath.Trim()));
        }

        public static ReportDocumentType ResolveCatalogReportType(string rawType, string templatePath)
        {
            if (Enum.TryParse(rawType, true, out ReportDocumentType reportType))
            {
                return reportType;
            }

            return string.Equals(NormalizeTemplateCatalogType(rawType, templatePath), InternalTemplateCatalogType, StringComparison.OrdinalIgnoreCase)
                ? ReportDocumentType.PaymentVoucher
                : ReportDocumentType.ExportDocument;
        }

        public static string NormalizeTemplateCatalogType(string rawType, string templatePath)
        {
            if (Enum.TryParse(rawType, true, out ReportDocumentType reportType))
            {
                return reportType == ReportDocumentType.PaymentVoucher
                    ? InternalTemplateCatalogType
                    : ExportTemplateCatalogType;
            }

            if (string.Equals(rawType, InternalTemplateCatalogType, StringComparison.OrdinalIgnoreCase))
            {
                return InternalTemplateCatalogType;
            }

            if (string.Equals(rawType, ExportTemplateCatalogType, StringComparison.OrdinalIgnoreCase))
            {
                return ExportTemplateCatalogType;
            }

            return DetermineTemplateCatalogType(templatePath);
        }

        public static string NormalizeTemplateDisplayName(string name, string templatePath)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return Path.GetFileNameWithoutExtension(templatePath) ?? string.Empty;
        }

        public static bool ValidateReportTemplateConfig(string json)
        {
            try
            {
                var normalizedJson = NormalizeReportTemplateConfigJson(json);
                using var document = JsonDocument.Parse(normalizedJson);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!root.TryGetProperty("reports", out var reports) || reports.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var report in reports.EnumerateArray())
                {
                    if (report.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    if (!report.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    if (!report.TryGetProperty("fileName", out var fileName) || fileName.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "验证报表模板配置 JSON 失败");
                return false;
            }
        }

        private static bool IsPreferredDefaultTemplate(string templatePath, ReportDocumentType reportType)
        {
            var fileName = Path.GetFileName(templatePath);
            return reportType switch
            {
                ReportDocumentType.PaymentVoucher => string.Equals(fileName, "payment_voucher_template.html", StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(fileName, "invoice_template.html", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static string DetermineTemplateCatalogType(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                return ExportTemplateCatalogType;
            }

            var normalizedPath = templatePath
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Trim();

            return normalizedPath.Contains($"{Path.DirectorySeparatorChar}{InternalTemplateCatalogType}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? InternalTemplateCatalogType
                : ExportTemplateCatalogType;
        }

        private static ReportTemplateConfig CloneTemplateConfig(ReportTemplateConfig config)
        {
            return new ReportTemplateConfig
            {
                Type = config.Type,
                FileName = config.FileName,
                Name = config.Name,
                WithSeal = config.WithSeal
            };
        }


        private static string NormalizeReportTemplateConfigJson(string json)
        {
            try
            {
                var node = JsonNode.Parse(json);
                if (node is not JsonObject obj)
                {
                    return json;
                }

                NormalizeObject(obj);
                return obj.ToJsonString();
            }
            catch
            {
                return json;
            }
        }

        private static void NormalizeObject(JsonObject obj)
        {
            var properties = obj.ToList();
            foreach (var kvp in properties)
            {
                var value = kvp.Value;
                if (value is JsonObject childObj)
                {
                    NormalizeObject(childObj);
                }
                else if (value is JsonArray arr)
                {
                    foreach (var item in arr)
                    {
                        if (item is JsonObject itemObj)
                        {
                            NormalizeObject(itemObj);
                        }
                    }
                }

                var newName = kvp.Key switch
                {
                    "Reports" => "reports",
                    "Type" => "type",
                    "FileName" => "fileName",
                    "Name" => "name",
                    "WithSeal" => "withSeal",
                    "PageSize" => "pageSize",
                    _ => kvp.Key
                };

                if (newName == kvp.Key)
                {
                    continue;
                }

                obj.Remove(kvp.Key);
                obj[newName] = value;
            }
        }
    }

    internal sealed class ReportTemplateConfigRoot
    {
        public List<ReportTemplateConfig> Reports { get; set; }
    }

    internal sealed class ReportTemplateConfig
    {
        public string Type { get; set; }

        public string FileName { get; set; }

        public string Name { get; set; }

        public bool? WithSeal { get; set; }
    }
}
