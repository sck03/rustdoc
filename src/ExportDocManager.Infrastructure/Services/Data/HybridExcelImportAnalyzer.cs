using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Infrastructure;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExportDocManager.Services.Data
{
    public sealed class HybridExcelImportAnalyzer : IExcelImportAnalyzer
    {
        public const string AnalyzerPathEnvironmentVariable = "EXPORTDOCMANAGER_EXCEL_ANALYZER";
        public const string AnalyzerModeEnvironmentVariable = "EXPORTDOCMANAGER_EXCEL_ANALYZER_MODE";

        private static readonly TimeSpan AnalyzerTimeout = TimeSpan.FromSeconds(30);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IAppPathProvider _pathProvider;
        private readonly BuiltInExcelImportAnalyzer _fallbackAnalyzer;

        public HybridExcelImportAnalyzer(
            IAppPathProvider pathProvider,
            BuiltInExcelImportAnalyzer fallbackAnalyzer)
        {
            _pathProvider = pathProvider;
            _fallbackAnalyzer = fallbackAnalyzer;
        }

        public async Task<ExcelImportAnalysisReport> AnalyzeAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default)
        {
            settings ??= new ExcelImportSettings();
            var analyzerMode = ResolveAnalyzerMode();
            if (analyzerMode != ExcelAnalyzerMode.BuiltIn)
            {
                return await AnalyzeExternalFirstAsync(filePath, settings, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await _fallbackAnalyzer
                .AnalyzeAsync(filePath, settings, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<ExcelImportAnalysisReport> AnalyzeExternalFirstAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken)
        {
            string analyzerPath = ResolveRustAnalyzerPath();
            if (string.IsNullOrWhiteSpace(analyzerPath))
            {
                return await _fallbackAnalyzer
                    .AnalyzeAsync(filePath, settings, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                var rustReport = await AnalyzeWithRustAsync(analyzerPath, filePath, cancellationToken)
                    .ConfigureAwait(false);

                if (!NeedsDotNetFusion(rustReport))
                {
                    return rustReport;
                }

                var builtInReport = await _fallbackAnalyzer
                    .AnalyzeAsync(filePath, settings, cancellationToken)
                    .ConfigureAwait(false);
                return MergeReports(rustReport, builtInReport);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var builtInReport = await _fallbackAnalyzer
                    .AnalyzeAsync(filePath, settings, cancellationToken)
                    .ConfigureAwait(false);
                builtInReport.Issues.Insert(0, new ExcelImportAnalysisIssue
                {
                    Severity = "Info",
                    Code = "ExternalAnalyzerFallback",
                    Message = $"外部 Excel 分析器不可用或未能解析该工作簿，已使用内置 .NET 模块继续预览: {ex.Message}"
                });
                return builtInReport;
            }
        }

        private async Task<ExcelImportAnalysisReport> AnalyzeWithRustAsync(
            string analyzerPath,
            string filePath,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AnalyzerTimeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = analyzerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(filePath);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Rust Excel 分析器进程启动失败。");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch
            {
                TryKillProcess(process);
                throw;
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"Rust Excel 分析器退出码 {process.ExitCode}。"
                        : stderr.Trim());
            }

            var rustReport = JsonSerializer.Deserialize<RustAnalysisReport>(stdout, JsonOptions)
                ?? throw new InvalidOperationException("Rust Excel 分析器返回了空报告。");

            return MapRustReport(rustReport, filePath);
        }

        private string ResolveRustAnalyzerPath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(AnalyzerPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            string toolsRoot = _pathProvider.ToolRoot;
            foreach (string fileName in GetRustAnalyzerFileNames())
            {
                string candidate = Path.Combine(toolsRoot, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static ExcelAnalyzerMode ResolveAnalyzerMode()
        {
            string configuredMode = Environment.GetEnvironmentVariable(AnalyzerModeEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredMode))
            {
                return ExcelAnalyzerMode.ExternalFirst;
            }

            return configuredMode.Trim().ToLowerInvariant() switch
            {
                "builtin" or "built-in" or "dotnet" or "module" => ExcelAnalyzerMode.BuiltIn,
                "auto" or "external" or "external-first" or "rust" or "rust-first" => ExcelAnalyzerMode.ExternalFirst,
                _ => ExcelAnalyzerMode.ExternalFirst
            };
        }

        private static IReadOnlyList<string> GetRustAnalyzerFileNames()
        {
            if (OperatingSystem.IsWindows())
            {
                return ["exportdoc-excel-analyzer.exe"];
            }

            return ["exportdoc-excel-analyzer"];
        }

        private static ExcelImportAnalysisReport MapRustReport(
            RustAnalysisReport rustReport,
            string filePath)
        {
            var report = new ExcelImportAnalysisReport
            {
                SchemaVersion = string.IsNullOrWhiteSpace(rustReport.SchemaVersion)
                    ? "excel-analysis-rs/unknown"
                    : rustReport.SchemaVersion,
                AnalyzerId = string.IsNullOrWhiteSpace(rustReport.AnalyzerId)
                    ? "rust-calamine"
                    : rustReport.AnalyzerId,
                SourcePath = Path.GetFullPath(filePath),
                SelectedWorksheetName = rustReport.SelectedWorksheetName ?? string.Empty,
                Confidence = ToDecimal(rustReport.Confidence),
                Sheets = rustReport.Sheets?.Select(sheet => new ExcelImportSheetAnalysis
                {
                    Name = sheet.Name ?? string.Empty,
                    UsedRowCount = sheet.UsedRange?.LastRow ?? 0,
                    UsedColumnCount = sheet.UsedRange?.LastColumn ?? 0,
                    FieldCandidateCount = sheet.FieldCandidates?.Count ?? 0,
                    HasItemTable = sheet.Table != null,
                    Confidence = ToDecimal(sheet.Confidence)
                }).ToList() ?? new List<ExcelImportSheetAnalysis>(),
                Fields = rustReport.Fields?.Select(field => new ExcelImportFieldAnalysis
                {
                    FieldKey = field.FieldKey ?? string.Empty,
                    DisplayName = field.DisplayName ?? string.Empty,
                    Value = field.Value ?? string.Empty,
                    WorksheetName = field.WorksheetName ?? string.Empty,
                    Row = field.Row,
                    Column = field.Column,
                    Confidence = ToDecimal(field.Confidence),
                    Source = field.Source ?? string.Empty
                }).ToList() ?? new List<ExcelImportFieldAnalysis>(),
                Issues = rustReport.Issues?.Select(issue => new ExcelImportAnalysisIssue
                {
                    Severity = issue.Severity ?? "Info",
                    Code = issue.Code ?? string.Empty,
                    Message = issue.Message ?? string.Empty,
                    FieldKey = issue.FieldKey ?? string.Empty
                }).ToList() ?? new List<ExcelImportAnalysisIssue>()
            };

            var selectedSheet = rustReport.Sheets?
                .FirstOrDefault(sheet => string.Equals(sheet.Name, report.SelectedWorksheetName, StringComparison.Ordinal));
            if (selectedSheet?.Table != null)
            {
                report.ItemTable = MapRustTable(report.SelectedWorksheetName, selectedSheet.Table);
            }

            return report;
        }

        private static ExcelImportItemTableAnalysis MapRustTable(string worksheetName, RustTableAnalysis table)
        {
            var columns = new ExcelImportItemColumnAnalysis();
            if (table.Fields != null)
            {
                foreach (var field in table.Fields)
                {
                    SetColumn(columns, field.CanonicalField, field.Column);
                }
            }

            return new ExcelImportItemTableAnalysis
            {
                WorksheetName = worksheetName ?? string.Empty,
                HeaderRow = table.HeaderStartRow,
                HeaderDepth = table.HeaderDepth,
                DataStartRow = table.DataStartRow,
                Confidence = ToDecimal(table.Confidence),
                Columns = columns
            };
        }

        private static void SetColumn(ExcelImportItemColumnAnalysis columns, string canonicalField, int column)
        {
            switch (canonicalField)
            {
                case "PoNumber": columns.PoNumberCol = PickColumn(columns.PoNumberCol, column); break;
                case "StyleNo": columns.StyleNoCol = PickColumn(columns.StyleNoCol, column); break;
                case "StyleName": columns.StyleNameCol = PickColumn(columns.StyleNameCol, column); break;
                case "FabricComposition": columns.FabricCompositionCol = PickColumn(columns.FabricCompositionCol, column); break;
                case "StyleNameCN": columns.StyleNameCNCol = PickColumn(columns.StyleNameCNCol, column); break;
                case "Brand": columns.BrandCol = PickColumn(columns.BrandCol, column); break;
                case "HSCode": columns.HSCodeCol = PickColumn(columns.HSCodeCol, column); break;
                case "Origin": columns.OriginCol = PickColumn(columns.OriginCol, column); break;
                case "Quantity": columns.QuantityCol = PickColumn(columns.QuantityCol, column); break;
                case "UnitEN": columns.UnitENCol = PickColumn(columns.UnitENCol, column); break;
                case "UnitCN": columns.UnitCNCol = PickColumn(columns.UnitCNCol, column); break;
                case "Cartons": columns.CartonsCol = PickColumn(columns.CartonsCol, column); break;
                case "CtnUnitEN": columns.CtnUnitENCol = PickColumn(columns.CtnUnitENCol, column); break;
                case "Length": columns.LengthCol = PickColumn(columns.LengthCol, column); break;
                case "Width": columns.WidthCol = PickColumn(columns.WidthCol, column); break;
                case "Height": columns.HeightCol = PickColumn(columns.HeightCol, column); break;
                case "Dimension": columns.DimensionCol = PickColumn(columns.DimensionCol, column); break;
                case "Volume": columns.VolumeCol = PickColumn(columns.VolumeCol, column); break;
                case "GWPerCtn": columns.GWPerCtnCol = PickColumn(columns.GWPerCtnCol, column); break;
                case "GWTotal": columns.GWTotalCol = PickColumn(columns.GWTotalCol, column); break;
                case "GrossWeight": columns.GWTotalCol = PickColumn(columns.GWTotalCol, column); break;
                case "NWPerCtn": columns.NWPerCtnCol = PickColumn(columns.NWPerCtnCol, column); break;
                case "NWTotal": columns.NWTotalCol = PickColumn(columns.NWTotalCol, column); break;
                case "NetWeight": columns.NWTotalCol = PickColumn(columns.NWTotalCol, column); break;
                case "UnitPrice": columns.UnitPriceCol = PickColumn(columns.UnitPriceCol, column); break;
                case "TotalPrice": columns.TotalPriceCol = PickColumn(columns.TotalPriceCol, column); break;
            }
        }

        private static int PickColumn(int current, int candidate)
        {
            return current > 0 || candidate <= 0 ? current : candidate;
        }

        private static decimal ToDecimal(double value)
        {
            return Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
        }

        private static bool NeedsDotNetFusion(ExcelImportAnalysisReport report)
        {
            if (report == null || report.Confidence < 0.85m || report.ItemTable == null)
            {
                return true;
            }

            string[] requiredFields =
            [
                "InvoiceNo",
                "CustomerNameEN",
                "ExporterNameEN",
                "PortOfLoading",
                "PortOfDestination"
            ];

            return requiredFields.Any(fieldKey => !HasConfidentField(report, fieldKey));
        }

        private static bool HasConfidentField(ExcelImportAnalysisReport report, string fieldKey)
        {
            return report.Fields?.Any(field =>
                string.Equals(field.FieldKey, fieldKey, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(field.Value)
                && field.Confidence >= 0.65m) == true;
        }

        private static ExcelImportAnalysisReport MergeReports(
            ExcelImportAnalysisReport primary,
            ExcelImportAnalysisReport fallback)
        {
            if (primary == null)
            {
                return fallback;
            }

            if (fallback == null)
            {
                return primary;
            }

            var mergedFields = new Dictionary<string, ExcelImportFieldAnalysis>(StringComparer.Ordinal);
            foreach (var field in primary.Fields ?? Enumerable.Empty<ExcelImportFieldAnalysis>())
            {
                if (!string.IsNullOrWhiteSpace(field.FieldKey))
                {
                    mergedFields[field.FieldKey] = field;
                }
            }

            foreach (var field in fallback.Fields ?? Enumerable.Empty<ExcelImportFieldAnalysis>())
            {
                if (string.IsNullOrWhiteSpace(field.FieldKey))
                {
                    continue;
                }

                if (!mergedFields.TryGetValue(field.FieldKey, out var current) || ShouldPreferFallbackField(current, field))
                {
                    mergedFields[field.FieldKey] = field;
                }
            }

            primary.Fields = mergedFields.Values
                .OrderBy(field => field.Row == 0 ? int.MaxValue : field.Row)
                .ThenBy(field => field.Column == 0 ? int.MaxValue : field.Column)
                .ThenBy(field => field.FieldKey, StringComparer.Ordinal)
                .ToList();

            if (ShouldPreferFallbackTable(primary.ItemTable, fallback.ItemTable))
            {
                primary.ItemTable = fallback.ItemTable;
            }

            if (string.IsNullOrWhiteSpace(primary.SelectedWorksheetName))
            {
                primary.SelectedWorksheetName = fallback.SelectedWorksheetName ?? string.Empty;
            }

            if ((primary.Sheets == null || primary.Sheets.Count == 0) && fallback.Sheets != null)
            {
                primary.Sheets = fallback.Sheets;
            }

            primary.Confidence = Math.Max(primary.Confidence, fallback.Confidence);
            primary.AnalyzerId = string.IsNullOrWhiteSpace(primary.AnalyzerId)
                ? fallback.AnalyzerId
                : $"{primary.AnalyzerId}+dotnet-fusion";
            primary.Issues ??= new List<ExcelImportAnalysisIssue>();
            primary.Issues.Insert(0, new ExcelImportAnalysisIssue
            {
                Severity = "Info",
                Code = "DotNetAnalyzerFusion",
                Message = ".NET Excel 分析器已补充 Rust 分析报告中的低置信字段或明细列。"
            });

            foreach (var issue in fallback.Issues ?? Enumerable.Empty<ExcelImportAnalysisIssue>())
            {
                if (!primary.Issues.Any(existing =>
                    string.Equals(existing.Code, issue.Code, StringComparison.Ordinal)
                    && string.Equals(existing.FieldKey, issue.FieldKey, StringComparison.Ordinal)
                    && string.Equals(existing.Message, issue.Message, StringComparison.Ordinal)))
                {
                    primary.Issues.Add(issue);
                }
            }

            return primary;
        }

        private static bool ShouldPreferFallbackField(
            ExcelImportFieldAnalysis current,
            ExcelImportFieldAnalysis fallback)
        {
            if (fallback == null || string.IsNullOrWhiteSpace(fallback.Value))
            {
                return false;
            }

            if (current == null || string.IsNullOrWhiteSpace(current.Value))
            {
                return true;
            }

            if (current.Confidence < 0.65m && fallback.Confidence >= current.Confidence)
            {
                return true;
            }

            if (fallback.Confidence > current.Confidence + 0.08m)
            {
                return true;
            }

            return IsMoreCompleteMultilineValue(fallback.Value, current.Value)
                && fallback.Confidence + 0.05m >= current.Confidence;
        }

        private static bool ShouldPreferFallbackTable(
            ExcelImportItemTableAnalysis current,
            ExcelImportItemTableAnalysis fallback)
        {
            if (fallback == null)
            {
                return false;
            }

            if (current == null)
            {
                return true;
            }

            int currentCount = CountMappedColumns(current.Columns);
            int fallbackCount = CountMappedColumns(fallback.Columns);
            return fallbackCount > currentCount
                || (fallbackCount == currentCount && fallback.Confidence > current.Confidence + 0.05m);
        }

        private static int CountMappedColumns(ExcelImportItemColumnAnalysis columns)
        {
            if (columns == null)
            {
                return 0;
            }

            return new[]
            {
                columns.PoNumberCol,
                columns.StyleNoCol,
                columns.StyleNameCol,
                columns.FabricCompositionCol,
                columns.StyleNameCNCol,
                columns.BrandCol,
                columns.HSCodeCol,
                columns.OriginCol,
                columns.QuantityCol,
                columns.UnitENCol,
                columns.UnitCNCol,
                columns.CartonsCol,
                columns.CtnUnitENCol,
                columns.LengthCol,
                columns.WidthCol,
                columns.HeightCol,
                columns.DimensionCol,
                columns.VolumeCol,
                columns.GWPerCtnCol,
                columns.GWTotalCol,
                columns.NWPerCtnCol,
                columns.NWTotalCol,
                columns.UnitPriceCol,
                columns.TotalPriceCol
            }.Count(column => column > 0);
        }

        private static bool IsMoreCompleteMultilineValue(string candidate, string current)
        {
            int candidateLines = CountNonEmptyLines(candidate);
            int currentLines = CountNonEmptyLines(current);
            return candidateLines > currentLines || candidate.Length > current.Length * 2;
        }

        private static int CountNonEmptyLines(string value)
        {
            return (value ?? string.Empty)
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        private enum ExcelAnalyzerMode
        {
            BuiltIn,
            ExternalFirst
        }

        private sealed class RustAnalysisReport
        {
            [JsonPropertyName("schema_version")]
            public string SchemaVersion { get; set; }

            [JsonPropertyName("analyzer_id")]
            public string AnalyzerId { get; set; }

            [JsonPropertyName("selected_worksheet_name")]
            public string SelectedWorksheetName { get; set; }

            [JsonPropertyName("confidence")]
            public double Confidence { get; set; }

            [JsonPropertyName("fields")]
            public List<RustFieldCandidate> Fields { get; set; }

            [JsonPropertyName("issues")]
            public List<RustIssue> Issues { get; set; }

            [JsonPropertyName("sheets")]
            public List<RustSheetAnalysis> Sheets { get; set; }
        }

        private sealed class RustSheetAnalysis
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("used_range")]
            public RustUsedRange UsedRange { get; set; }

            [JsonPropertyName("confidence")]
            public double Confidence { get; set; }

            [JsonPropertyName("field_candidates")]
            public List<RustFieldCandidate> FieldCandidates { get; set; }

            [JsonPropertyName("table")]
            public RustTableAnalysis Table { get; set; }
        }

        private sealed class RustUsedRange
        {
            [JsonPropertyName("last_row")]
            public int LastRow { get; set; }

            [JsonPropertyName("last_column")]
            public int LastColumn { get; set; }
        }

        private sealed class RustTableAnalysis
        {
            [JsonPropertyName("header_start_row")]
            public int HeaderStartRow { get; set; }

            [JsonPropertyName("header_depth")]
            public int HeaderDepth { get; set; }

            [JsonPropertyName("data_start_row")]
            public int DataStartRow { get; set; }

            [JsonPropertyName("confidence")]
            public double Confidence { get; set; }

            [JsonPropertyName("fields")]
            public List<RustTableField> Fields { get; set; }
        }

        private sealed class RustTableField
        {
            [JsonPropertyName("canonical_field")]
            public string CanonicalField { get; set; }

            [JsonPropertyName("column")]
            public int Column { get; set; }
        }

        private sealed class RustFieldCandidate
        {
            [JsonPropertyName("field_key")]
            public string FieldKey { get; set; }

            [JsonPropertyName("display_name")]
            public string DisplayName { get; set; }

            [JsonPropertyName("value")]
            public string Value { get; set; }

            [JsonPropertyName("worksheet_name")]
            public string WorksheetName { get; set; }

            [JsonPropertyName("row")]
            public int Row { get; set; }

            [JsonPropertyName("column")]
            public int Column { get; set; }

            [JsonPropertyName("confidence")]
            public double Confidence { get; set; }

            [JsonPropertyName("source")]
            public string Source { get; set; }
        }

        private sealed class RustIssue
        {
            [JsonPropertyName("severity")]
            public string Severity { get; set; }

            [JsonPropertyName("code")]
            public string Code { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("field_key")]
            public string FieldKey { get; set; }
        }
    }
}
