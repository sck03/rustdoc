namespace ExportDocManager.Models.DTOs
{
    public sealed class ReportTemplateSelectionResult
    {
        public string TemplatePath { get; init; } = string.Empty;

        public bool WithSeal { get; init; }
    }
}
