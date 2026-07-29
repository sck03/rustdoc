namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowClientProfileUpdate
    {
        public string ProfileKey { get; init; } = string.Empty;

        public string ProfileName { get; init; } = string.Empty;

        public string CompanyScope { get; init; } = string.Empty;

        public string CardIdentifier { get; init; } = string.Empty;

        public string CustomsCooClientRootPath { get; init; } = string.Empty;

        public string AgentConsignmentClientRootPath { get; init; } = string.Empty;

        public bool CanSubmitCustomsCoo { get; init; } = true;

        public bool CanSubmitAgentConsignment { get; init; } = true;
    }

    public sealed class SingleWindowClientDispatchResult
    {
        public int BatchId { get; init; }

        public string BatchReference { get; init; } = string.Empty;

        public string TargetDirectory { get; init; } = string.Empty;

        public string ProfileName { get; init; } = string.Empty;

        public int PayloadFileCount { get; init; }

        public int AttachmentFileCount { get; init; }
    }

    public sealed class SingleWindowReceiptCollectionResult
    {
        public int BatchId { get; init; }

        public string BatchReference { get; init; } = string.Empty;

        public string ReceiptRootPath { get; init; } = string.Empty;

        public IReadOnlyList<string> ReceiptFiles { get; init; } = [];
    }
}
