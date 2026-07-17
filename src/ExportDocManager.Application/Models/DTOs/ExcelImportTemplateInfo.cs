namespace ExportDocManager.Models.DTOs
{
    public sealed class ExcelImportTemplateInfo
    {
        public string DisplayName { get; init; } = string.Empty;

        public string ResourceRelativePath { get; init; } = string.Empty;

        public string DefaultFileName { get; init; } = string.Empty;
    }
}
