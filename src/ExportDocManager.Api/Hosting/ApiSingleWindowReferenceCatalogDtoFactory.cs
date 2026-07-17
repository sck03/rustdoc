using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public const string ReferenceCatalogStoragePolicy =
            "单一窗口内置参考词典只读程序根 Resources/SingleWindow；覆盖词典写入运行数据根 SingleWindow/singlewindow_reference_catalogs.override.json，不写系统用户目录、全局程序数据目录或系统 C 盘默认落点。";

        public const string ReferenceCatalogExcelImportStoragePolicy =
            "Excel 上传导入只解析请求体中的工作簿内容并返回当前分类预览，不接收服务器本地任意路径、不写临时文件；应用到草稿后仍需保存覆盖词典，覆盖文件写入运行数据根 SingleWindow/singlewindow_reference_catalogs.override.json。";

        public static ApiSingleWindowReferenceCatalogResponse FromReferenceCatalog(
            SingleWindowReferenceCatalogModel catalog)
        {
            return new ApiSingleWindowReferenceCatalogResponse(
                catalog ?? new SingleWindowReferenceCatalogModel(),
                ReferenceCatalogStoragePolicy);
        }

        public static ApiSingleWindowReferenceCatalogSaveResponse FromSavedReferenceCatalog(
            SingleWindowReferenceCatalogModel catalog,
            string message)
        {
            return new ApiSingleWindowReferenceCatalogSaveResponse(
                true,
                catalog ?? new SingleWindowReferenceCatalogModel(),
                string.IsNullOrWhiteSpace(message) ? "单一窗口参考词典已保存。" : message,
                ReferenceCatalogStoragePolicy);
        }

        public static ApiSingleWindowReferenceCatalogExcelImportPreviewResponse FromReferenceCatalogExcelPreview(
            SingleWindowReferenceCatalogExcelImportPreview preview)
        {
            return new ApiSingleWindowReferenceCatalogExcelImportPreviewResponse(
                true,
                preview.CatalogKey,
                preview.SheetName,
                preview.SheetNames,
                preview.HeaderRowNumber,
                preview.DataStartRowNumber,
                preview.ColumnMappings
                    .Select(item => new ApiSingleWindowReferenceCatalogExcelColumnMappingDto(
                        item.FieldKey,
                        item.Label,
                        item.ColumnNumber,
                        item.Required))
                    .ToArray(),
                preview.Catalog ?? new SingleWindowReferenceCatalogModel(),
                preview.RowCount,
                preview.RowCount > 0
                    ? $"已从工作表“{preview.SheetName}”读取 {preview.RowCount} 行。"
                    : $"工作表“{preview.SheetName}”没有读到可导入行。",
                ReferenceCatalogExcelImportStoragePolicy);
        }
    }
}
