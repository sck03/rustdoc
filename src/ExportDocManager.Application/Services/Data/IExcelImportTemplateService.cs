using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Data
{
    public interface IExcelImportTemplateService
    {
        ExcelImportTemplateInfo GetDefaultTemplate();

        string EnsureDefaultTemplateAvailable();

        string ExportDefaultTemplate(string targetFilePath, bool overwrite = true);

        string ExportBlankBookingSheet(string targetFilePath, bool overwrite = true);

        string ExportBookingSheet(string sourceFilePath, string targetFilePath, bool overwrite = true);

        string ExportBookingSheetFromInvoice(Invoice invoice, string targetFilePath, bool overwrite = true);
    }
}
