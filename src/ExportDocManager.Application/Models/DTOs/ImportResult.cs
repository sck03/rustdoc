using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public class ImportResult
    {
        public Invoice Invoice { get; set; } = new();
        public Customer Customer { get; set; } = new();
        public Exporter Exporter { get; set; } = new();
        public ExcelImportAnalysisReport AnalysisReport { get; set; } = new();
        public List<string> Errors { get; set; } = new List<string>();
        public bool Success => Errors.Count == 0;
    }
}
