using ExportDocManager.Models.Entities;

namespace ExportDocManager.Models.DTOs
{
    public sealed class MainWorkspaceSaveRequest
    {
        public Invoice Invoice { get; init; } = new();

        public IReadOnlyList<Item> Items { get; init; } = Array.Empty<Item>();

        public Customer Customer { get; init; } = new();

        public Exporter Exporter { get; init; } = new();
    }

    public sealed class MainWorkspaceSaveResult
    {
        public bool Success { get; init; }

        public Invoice SavedInvoice { get; init; } = new();

        public bool IsUpdate { get; init; }
    }

    public sealed class MainExcelImportWorkflowResult : ImportResult
    {
        public bool HasSelectedFile { get; init; }
    }
}
