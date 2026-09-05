using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ExportDocManager.Utils;
using Microsoft.Extensions.Logging;

namespace ExportDocManager.Services.Reporting
{
    internal sealed class ReportTemplateCatalogLoader
    {
        public const string ExportTemplateCatalogType = "Export";
        public const string InternalTemplateCatalogType = "Internal";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ReportTemplatePathResolver _pathResolver;
        private readonly ILogger? _logger;

        public ReportTemplateCatalogLoader(
            ReportTemplatePathResolver pathResolver,
            ILogger? logger = null)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _logger = logger;
        }

        public async Task<IReadOnlyList<ReportTemplateConfig>> LoadResolvedConfigsAsync(
            CancellationToken cancellationToken = default)
        {
            string configPath = _pathResolver.GetUserConfigPath();
            var configuredRows = new List<ReportTemplateConfig>();

            if (File.Exists(configPath))
            {
                string json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                if (ValidateReportTemplateConfig(json, _logger))
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

                            string path = _pathResolver.ToAbsolutePath(cfg.FileName);

                            configuredRows.Add(new ReportTemplateConfig
                            {
                                Type = DetermineTemplateCatalogType(path),
                                FileName = Path.GetFullPath(path),
                                Name = cfg.Name,
                                WithSeal = cfg.WithSeal
                            });
                        }
                    }
                }
                else
                {
                    throw new InvalidDataException("报表模板配置文件格式无效，已拒绝回退到其它模板。");
                }
            }

            return BuildResolvedTemplateConfigs(configuredRows, cancellationToken);
        }

        public List<ReportTemplateConfig> BuildResolvedTemplateConfigs(
            IEnumerable<ReportTemplateConfig> configuredRows,
            CancellationToken cancellationToken = default)
        {
            string builtInRoot = _pathResolver.GetBuiltInTemplatesBaseDirectory();
            string userRoot = _pathResolver.GetUserTemplatesBaseDirectory();
            var configuredByPath = new Dictionary<string, ReportTemplateConfig>(PhysicalPathComparison.Comparer);
            var resolvedByIdentity = new Dictionary<string, ReportTemplateConfig>(PortablePathKey.Comparer);

            foreach (var row in configuredRows ?? Enumerable.Empty<ReportTemplateConfig>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.FileName))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(row.FileName);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("报表模板配置引用的文件不存在。", fullPath);
                }

                if (!_pathResolver.IsBuiltInTemplatePath(fullPath) && !_pathResolver.IsUserTemplatePath(fullPath))
                {
                    throw new InvalidDataException("报表模板配置引用了受管模板目录之外的文件。");
                }

                string catalogType = DetermineTemplateCatalogType(fullPath);
                configuredByPath[fullPath] = new ReportTemplateConfig
                {
                    Type = catalogType,
                    FileName = fullPath,
                    Name = NormalizeTemplateDisplayName(row.Name, fullPath),
                    WithSeal = ResolveCatalogReportType(catalogType, fullPath) == ReportDocumentType.PaymentVoucher
                        ? null
                        : row.WithSeal ?? true
                };
            }

            AddTemplatesFromRoot(builtInRoot, configuredByPath, resolvedByIdentity, cancellationToken);
            AddTemplatesFromRoot(userRoot, configuredByPath, resolvedByIdentity, cancellationToken);

            return resolvedByIdentity.Values
                .OrderBy(config => NormalizeTemplateCatalogType(config.Type, config.FileName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(config => NormalizeTemplateDisplayName(config.Name, config.FileName), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void AddTemplatesFromRoot(
            string root,
            IReadOnlyDictionary<string, ReportTemplateConfig> configuredByPath,
            IDictionary<string, ReportTemplateConfig> resolvedByIdentity,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            var identitiesInRoot = new HashSet<string>(PortablePathKey.Comparer);
            foreach (string templatePath in ReportTemplateFilePolicy.EnumerateTemplates(root)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.GetFullPath(templatePath);
                string identity = _pathResolver.GetCatalogIdentity(fullPath);
                if (!identitiesInRoot.Add(identity))
                {
                    throw new InvalidDataException($"报表模板存在跨平台大小写或 Unicode 冲突：{identity}");
                }

                resolvedByIdentity[identity] = configuredByPath.TryGetValue(fullPath, out var configured)
                    ? CloneTemplateConfig(configured)
                    : new ReportTemplateConfig
                    {
                        Type = NormalizeTemplateCatalogType(null, fullPath),
                        FileName = fullPath,
                        Name = NormalizeTemplateDisplayName(null, fullPath),
                        WithSeal = ResolveCatalogReportType(null, fullPath) == ReportDocumentType.PaymentVoucher
                            ? null
                            : true
                    };
            }
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
                    GetTemplatePriority(fullPath, reportType) > GetTemplatePriority(existingPath, reportType))
                {
                    cache[reportType] = fullPath;
                }
            }

            return cache;
        }

        private int GetTemplatePriority(string templatePath, ReportDocumentType reportType)
        {
            int priority = _pathResolver.IsUserTemplatePath(templatePath) ? 100 : 0;
            return IsPreferredDefaultTemplate(templatePath, reportType) ? priority + 10 : priority;
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

        public static ReportDocumentType ResolveCatalogReportType(string? rawType, string? templatePath)
        {
            if (Enum.TryParse(rawType, true, out ReportDocumentType reportType))
            {
                return reportType;
            }

            return string.Equals(NormalizeTemplateCatalogType(rawType, templatePath), InternalTemplateCatalogType, StringComparison.OrdinalIgnoreCase)
                ? ReportDocumentType.PaymentVoucher
                : ReportDocumentType.ExportDocument;
        }

        public static string NormalizeTemplateCatalogType(string? rawType, string? templatePath)
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

        public static string NormalizeTemplateDisplayName(string? name, string? templatePath)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return Path.GetFileNameWithoutExtension(templatePath) ?? string.Empty;
        }

        public static bool ValidateReportTemplateConfig(string json, ILogger? logger = null)
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
                logger?.LogError(ex, "验证报表模板配置 JSON 失败");
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

        private static string DetermineTemplateCatalogType(string? templatePath)
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
        public List<ReportTemplateConfig> Reports { get; set; } = [];
    }

    internal sealed class ReportTemplateConfig
    {
        public string Type { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool? WithSeal { get; set; }
    }
}
