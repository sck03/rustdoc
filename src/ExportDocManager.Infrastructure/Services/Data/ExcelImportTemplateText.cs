namespace ExportDocManager.Services.Data
{
    internal static class ExcelImportTemplateText
    {
        public const string ExporterPlaceholderText = "请填写出口商中文名称";

        public static bool IsExporterPlaceholder(string value)
        {
            return string.Equals(
                value?.Trim(),
                ExporterPlaceholderText,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
