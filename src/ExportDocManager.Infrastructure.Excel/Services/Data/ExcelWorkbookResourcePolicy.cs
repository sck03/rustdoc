using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace ExportDocManager.Services.Data
{
    internal sealed record ExcelWorkbookResourceLimits(
        long MaximumPackageBytes = 25L * 1024L * 1024L,
        int MaximumEntries = 2_048,
        long MaximumEntryBytes = 64L * 1024L * 1024L,
        long MaximumTotalExpandedBytes = 256L * 1024L * 1024L,
        double MaximumCompressionRatio = 500d,
        int MaximumWorksheets = 64,
        int MaximumRowsPerWorksheet = 100_000,
        int MaximumColumnsPerWorksheet = 512,
        long MaximumTotalCells = 1_000_000);

    public static class ExcelWorkbookResourcePolicy
    {
        private static readonly ExcelWorkbookResourceLimits DefaultLimits = new();

        public static void Validate(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            string normalizedPath = Path.GetFullPath(filePath);
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("Excel 工作簿不存在。", normalizedPath);
            }

            using var input = new FileStream(
                normalizedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.SequentialScan);
            Validate(input, normalizedPath);
        }

        public static void Validate(Stream input, string fileName)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            if (IsOpenXmlWorkbook(fileName))
            {
                ValidateOpenXmlPackage(input, DefaultLimits);
                return;
            }

            if (IsBinaryWorkbook(fileName))
            {
                ValidateInputStream(input, DefaultLimits);
                return;
            }

            throw new InvalidDataException("仅支持 .xls、.xlsx、.xlsm、.xltx 和 .xltm 工作簿。");
        }

        public static void ValidateOpenXmlPackage(Stream input)
        {
            ValidateOpenXmlPackage(input, DefaultLimits);
        }

        internal static ExcelWorkbookResourceBudget CreateLogicalBudget()
        {
            return new ExcelWorkbookResourceBudget(DefaultLimits);
        }

        internal static void ValidateOpenXmlPackage(
            Stream input,
            ExcelWorkbookResourceLimits limits)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(limits);
            ValidateLimits(limits);
            ValidateInputStream(input, limits);

            long originalPosition = input.Position;
            try
            {
                input.Position = 0;
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException("Excel 工作簿压缩包不包含任何条目。");
                }
                if (archive.Entries.Count > limits.MaximumEntries)
                {
                    throw new InvalidDataException(
                        $"Excel 工作簿条目数超过 {limits.MaximumEntries} 个上限。");
                }

                long totalExpandedBytes = 0;
                var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var worksheetEntries = new List<ZipArchiveEntry>();
                foreach (var entry in archive.Entries)
                {
                    string entryName = NormalizeEntryName(entry.FullName);
                    if (!entryNames.Add(entryName))
                    {
                        throw new InvalidDataException($"Excel 工作簿包含重复条目：{entryName}");
                    }
                    if (entry.Length < 0 || entry.CompressedLength < 0)
                    {
                        throw new InvalidDataException($"Excel 工作簿条目大小无效：{entryName}");
                    }
                    if (entry.Length > limits.MaximumEntryBytes)
                    {
                        throw new InvalidDataException(
                            $"Excel 工作簿条目展开后超过 {limits.MaximumEntryBytes / (1024 * 1024)} MB 上限：{entryName}");
                    }
                    if (totalExpandedBytes > limits.MaximumTotalExpandedBytes - entry.Length)
                    {
                        throw new InvalidDataException(
                            $"Excel 工作簿展开总大小超过 {limits.MaximumTotalExpandedBytes / (1024 * 1024)} MB 上限。");
                    }
                    totalExpandedBytes += entry.Length;

                    if (entry.Length > 1024L * 1024L)
                    {
                        double ratio = entry.CompressedLength == 0
                            ? double.PositiveInfinity
                            : (double)entry.Length / entry.CompressedLength;
                        if (ratio > limits.MaximumCompressionRatio)
                        {
                            throw new InvalidDataException(
                                $"Excel 工作簿条目压缩比异常：{entryName}");
                        }
                    }

                    if (entryName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                        && entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        worksheetEntries.Add(entry);
                    }
                }

                var budget = new ExcelWorkbookResourceBudget(limits);
                foreach (var worksheetEntry in worksheetEntries)
                {
                    ValidateWorksheetXml(worksheetEntry, budget);
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException("Excel 工作簿工作表 XML 无效。", ex);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            {
                throw new InvalidDataException("Excel 工作簿压缩结构无效。", ex);
            }
            finally
            {
                input.Position = originalPosition;
            }
        }

        private static void ValidateInputStream(Stream input, ExcelWorkbookResourceLimits limits)
        {
            ValidateLimits(limits);
            if (!input.CanRead || !input.CanSeek)
            {
                throw new InvalidDataException("Excel 工作簿输入流必须支持读取和定位。");
            }

            long packageBytes = input.Length;
            if (packageBytes <= 0)
            {
                throw new InvalidDataException("Excel 工作簿不能为空。");
            }
            if (packageBytes > limits.MaximumPackageBytes)
            {
                throw new InvalidDataException(
                    $"Excel 工作簿大小超过 {limits.MaximumPackageBytes / (1024 * 1024)} MB 上限。");
            }
        }

        private static void ValidateWorksheetXml(
            ZipArchiveEntry worksheetEntry,
            ExcelWorkbookResourceBudget workbookBudget)
        {
            string worksheetName = Path.GetFileNameWithoutExtension(worksheetEntry.Name);
            ExcelWorksheetResourceBudget worksheetBudget = workbookBudget.StartWorksheet(worksheetName);
            using Stream stream = worksheetEntry.Open();
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                XmlResolver = null
            });

            int observedRow = 0;
            int currentRow = 0;
            int currentMaximumColumn = 0;
            int currentCellCount = 0;
            bool insideRow = false;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
                {
                    FlushRow();
                    observedRow++;
                    string? rowReference = reader.GetAttribute("r");
                    currentRow = string.IsNullOrWhiteSpace(rowReference)
                        ? observedRow
                        : ParsePositiveRowNumber(rowReference, "工作表行号");
                    currentMaximumColumn = 0;
                    currentCellCount = 0;
                    insideRow = true;
                    if (reader.IsEmptyElement)
                    {
                        FlushRow();
                    }
                    continue;
                }

                if (insideRow && reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
                {
                    currentCellCount++;
                    (int column, int row) = ParseCellReference(reader.GetAttribute("r"));
                    if (row > 0)
                    {
                        currentRow = Math.Max(currentRow, row);
                    }
                    currentMaximumColumn = Math.Max(
                        currentMaximumColumn,
                        column > 0 ? column : currentCellCount);
                    continue;
                }

                if (insideRow && reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row")
                {
                    FlushRow();
                }
            }

            FlushRow();
            return;

            void FlushRow()
            {
                if (!insideRow)
                {
                    return;
                }

                worksheetBudget.RegisterRow(currentRow, currentMaximumColumn, currentCellCount);
                insideRow = false;
            }
        }

        private static int ParsePositiveRowNumber(string value, string description)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            {
                throw new InvalidDataException($"Excel 工作簿{description}无效：{value}。");
            }

            return parsed;
        }

        private static (int Column, int Row) ParseCellReference(string? cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                return (0, 0);
            }

            int column = 0;
            int index = 0;
            while (index < cellReference.Length && char.IsLetter(cellReference[index]))
            {
                char normalized = char.ToUpperInvariant(cellReference[index]);
                if (normalized is < 'A' or > 'Z')
                {
                    throw new InvalidDataException($"Excel 工作簿单元格引用无效：{cellReference}。");
                }

                int value = normalized - 'A' + 1;
                if (column > (int.MaxValue - value) / 26)
                {
                    column = int.MaxValue;
                    while (index < cellReference.Length && char.IsLetter(cellReference[index]))
                    {
                        index++;
                    }
                    break;
                }

                column = (column * 26) + value;
                index++;
            }

            if (column == 0 || index >= cellReference.Length)
            {
                throw new InvalidDataException($"Excel 工作簿单元格引用无效：{cellReference}。");
            }

            string rowReference = cellReference[index..];
            int row = ParsePositiveRowNumber(rowReference, "单元格行号");
            return (column, row);
        }

        private static bool IsOpenXmlWorkbook(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBinaryWorkbook(string fileName)
        {
            return Path.GetExtension(fileName ?? string.Empty)
                .Equals(".xls", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeEntryName(string entryName)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/').Trim('/');
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
            {
                throw new InvalidDataException("Excel 工作簿包含无效条目路径。");
            }
            return string.Join('/', segments);
        }

        private static void ValidateLimits(ExcelWorkbookResourceLimits limits)
        {
            if (limits.MaximumPackageBytes <= 0
                || limits.MaximumEntries <= 0
                || limits.MaximumEntryBytes <= 0
                || limits.MaximumTotalExpandedBytes <= 0
                || limits.MaximumEntryBytes > limits.MaximumTotalExpandedBytes
                || limits.MaximumCompressionRatio < 1d
                || limits.MaximumWorksheets <= 0
                || limits.MaximumRowsPerWorksheet <= 0
                || limits.MaximumColumnsPerWorksheet <= 0
                || limits.MaximumTotalCells <= 0)
            {
                throw new ArgumentException("Excel 工作簿资源配额无效。", nameof(limits));
            }
        }
    }

    internal sealed class ExcelWorkbookResourceBudget
    {
        private readonly ExcelWorkbookResourceLimits _limits;
        private long _totalCells;
        private int _worksheetCount;

        public ExcelWorkbookResourceBudget(ExcelWorkbookResourceLimits limits)
        {
            _limits = limits;
        }

        public ExcelWorksheetResourceBudget StartWorksheet(string worksheetName)
        {
            int worksheetCount = checked(++_worksheetCount);
            if (worksheetCount > _limits.MaximumWorksheets)
            {
                throw new InvalidDataException(
                    $"Excel 工作簿工作表数量超过 {_limits.MaximumWorksheets} 个上限。");
            }

            return new ExcelWorksheetResourceBudget(this, worksheetName, _limits);
        }

        internal void RegisterCells(string worksheetName, int cellCount)
        {
            if (_totalCells > _limits.MaximumTotalCells - cellCount)
            {
                throw new InvalidDataException(
                    $"Excel 工作簿单元格总数超过 {_limits.MaximumTotalCells} 个上限（工作表：{worksheetName}）。");
            }

            _totalCells += cellCount;
        }
    }

    internal sealed class ExcelWorksheetResourceBudget
    {
        private readonly ExcelWorkbookResourceBudget _workbookBudget;
        private readonly string _worksheetName;
        private readonly ExcelWorkbookResourceLimits _limits;

        public ExcelWorksheetResourceBudget(
            ExcelWorkbookResourceBudget workbookBudget,
            string worksheetName,
            ExcelWorkbookResourceLimits limits)
        {
            _workbookBudget = workbookBudget;
            _worksheetName = string.IsNullOrWhiteSpace(worksheetName) ? "未命名" : worksheetName;
            _limits = limits;
        }

        public void RegisterRow(int rowNumber, int maximumColumn, int cellCount)
        {
            if (rowNumber <= 0 || rowNumber > _limits.MaximumRowsPerWorksheet)
            {
                throw new InvalidDataException(
                    $"Excel 工作表“{_worksheetName}”行数超过 {_limits.MaximumRowsPerWorksheet} 行上限。");
            }
            if (maximumColumn < 0 || maximumColumn > _limits.MaximumColumnsPerWorksheet)
            {
                throw new InvalidDataException(
                    $"Excel 工作表“{_worksheetName}”列数超过 {_limits.MaximumColumnsPerWorksheet} 列上限。");
            }
            if (cellCount < 0)
            {
                throw new InvalidDataException($"Excel 工作表“{_worksheetName}”单元格数量无效。");
            }

            _workbookBudget.RegisterCells(_worksheetName, cellCount);
        }
    }
}
