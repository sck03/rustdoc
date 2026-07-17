namespace ExportDocManager.Models.DTOs.SingleWindow
{
    public enum SingleWindowExportIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public enum SingleWindowExportReviewDialogAction
    {
        Cancel = 0,
        ContinueExport = 1,
        RepairGroups = 2,
        OpenEditor = 3
    }

    public sealed class SingleWindowExportIssue
    {
        public string GroupKey { get; init; } = string.Empty;

        public string GroupDisplayName { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public SingleWindowExportIssueSeverity Severity { get; init; } = SingleWindowExportIssueSeverity.Warning;

        public bool CanAutoRepair { get; init; }

        public SingleWindowEditorNavigationTarget NavigationTarget { get; init; } = new();
    }

    public sealed class SingleWindowExportIssueGroup
    {
        public string GroupKey { get; init; } = string.Empty;

        public string GroupDisplayName { get; init; } = string.Empty;

        public bool CanAutoRepair { get; init; }

        public IReadOnlyList<SingleWindowExportIssue> Issues { get; init; } = [];

        public int ErrorCount => Issues.Count(item => item.Severity == SingleWindowExportIssueSeverity.Error);

        public int WarningCount => Issues.Count(item => item.Severity == SingleWindowExportIssueSeverity.Warning);

        public int InfoCount => Issues.Count(item => item.Severity == SingleWindowExportIssueSeverity.Info);
    }

    public sealed class SingleWindowExportReview
    {
        public SingleWindowBusinessType BusinessType { get; init; }

        public int InvoiceId { get; init; }

        public string InvoiceNo { get; init; } = string.Empty;

        public string ContractNo { get; init; } = string.Empty;

        public int DraftRevision { get; init; }

        public int ManualLockedFieldCount { get; init; }

        public int SourceDiffCount { get; init; }

        public string SourceDiffSummary { get; init; } = string.Empty;

        public IReadOnlyList<SingleWindowExportIssueGroup> Groups { get; init; } = [];

        public int TotalErrorCount => Groups.Sum(group => group.ErrorCount);

        public int TotalWarningCount => Groups.Sum(group => group.WarningCount);

        public bool HasIssues => Groups.Count > 0;
    }

    public sealed class SingleWindowExportReviewDialogResult
    {
        public SingleWindowExportReviewDialogAction Action { get; init; }

        public IReadOnlyList<string> SelectedGroupKeys { get; init; } = [];

        public SingleWindowEditorNavigationTarget NavigationTarget { get; init; } = new();
    }

    public sealed class SingleWindowEditorNavigationTarget
    {
        public string GroupKey { get; init; } = string.Empty;

        public string PropertyKey { get; init; } = string.Empty;

        public int GoodsLineNo { get; init; }
    }
}
