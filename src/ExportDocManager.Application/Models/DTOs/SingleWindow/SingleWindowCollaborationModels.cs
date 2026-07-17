namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public sealed class SingleWindowCollaborationQuery
    {
        public string BusinessType { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Keyword { get; init; } = string.Empty;

        public int Take { get; init; } = 200;
    }

    public sealed class SingleWindowOperationTicketRow
    {
        public int TicketId { get; init; }

        public string BusinessType { get; init; } = string.Empty;

        public int SourceInvoiceId { get; init; }

        public int DocumentId { get; init; }

        public int? BatchId { get; init; }

        public string Status { get; init; } = string.Empty;

        public string RequestedBy { get; init; } = string.Empty;

        public string AssignedOperator { get; init; } = string.Empty;

        public int? AssignedWorkstationId { get; init; }

        public int Priority { get; init; }

        public DateTime RequestedAt { get; init; }

        public DateTime? AssignedAt { get; init; }

        public DateTime? SubmittedAt { get; init; }

        public DateTime? CompletedAt { get; init; }

        public string LastError { get; init; } = string.Empty;
    }

    public sealed class SingleWindowWorkstationRow
    {
        public int WorkstationId { get; init; }

        public string MachineName { get; init; } = string.Empty;

        public int? ProfileId { get; init; }

        public string OperatorName { get; init; } = string.Empty;

        public bool CanSubmitAgentConsignment { get; init; }

        public bool CanSubmitCustomsCoo { get; init; }

        public bool IsEnabled { get; init; }

        public string Remarks { get; init; } = string.Empty;

        public DateTime UpdatedAt { get; init; }
    }
}
