using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Data
{
    public interface IExcelImportTemplateService
    {
        ExcelImportTemplateInfo GetDefaultTemplate();

        string EnsureDefaultTemplateAvailable();

        Task<string> ExportDefaultTemplateAsync(
            string targetFilePath,
            bool overwrite = true,
            CancellationToken cancellationToken = default);

        Task<string> ExportBlankBookingSheetAsync(
            string targetFilePath,
            bool overwrite = true,
            CancellationToken cancellationToken = default);

        string ExportBookingSheet(string sourceFilePath, string targetFilePath, bool overwrite = true);

        string ExportBookingSheetFromInvoice(Invoice invoice, string targetFilePath, bool overwrite = true);
    }
}
