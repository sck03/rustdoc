using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public class ImportResult
    {
        public Invoice Invoice { get; set; }
        public Customer Customer { get; set; }
        public Exporter Exporter { get; set; }
        public ExcelImportAnalysisReport AnalysisReport { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public bool Success => Errors.Count == 0;
    }
}
