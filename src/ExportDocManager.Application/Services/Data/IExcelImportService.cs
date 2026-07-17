using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Data
{
    public interface IExcelImportAnalyzer
    {
        Task<ExcelImportAnalysisReport> AnalyzeAsync(
            string filePath,
            ExcelImportSettings settings,
            CancellationToken cancellationToken = default);
    }

    public interface IExcelImportService
    {
        Task<ImportResult> ImportFromExcelAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
