using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public sealed class MainWorkspaceSaveRequest
    {
        public Invoice Invoice { get; init; }

        public IReadOnlyList<Item> Items { get; init; } = Array.Empty<Item>();

        public Customer Customer { get; init; }

        public Exporter Exporter { get; init; }
    }

    public sealed class MainWorkspaceSaveResult
    {
        public bool Success { get; init; }

        public Invoice SavedInvoice { get; init; }

        public bool IsUpdate { get; init; }
    }

    public sealed class MainExcelImportWorkflowResult : ImportResult
    {
        public bool HasSelectedFile { get; init; }
    }
}
