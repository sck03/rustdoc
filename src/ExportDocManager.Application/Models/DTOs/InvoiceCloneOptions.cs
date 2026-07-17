namespace ExportDocManager.Models.DTOs
{
    public class InvoiceCloneOptions
    {
        public bool CopyHeader { get; set; } = true;
        public bool CopyItems { get; set; } = true;
        public bool ResetStatus { get; set; } = true;
        public bool ResetDates { get; set; } = true;
        public bool ClearAmounts { get; set; }
    }
}
