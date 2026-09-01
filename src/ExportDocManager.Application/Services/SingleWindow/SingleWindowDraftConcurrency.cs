using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.SingleWindow;

/// <summary>
/// Raised when an editor attempts to save a draft based on an older revision.
/// The current revision is included so clients can refresh deliberately instead
/// of silently overwriting another user's work.
/// </summary>
public sealed class SingleWindowDraftConcurrencyException : ServiceConcurrencyException
{
    public SingleWindowDraftConcurrencyException(
        string message,
        int sourceInvoiceId,
        int currentRevision,
        Exception? innerException = null)
        : base(message, innerException)
    {
        SourceInvoiceId = sourceInvoiceId;
        CurrentRevision = Math.Max(0, currentRevision);
    }

    public int SourceInvoiceId { get; }

    public int CurrentRevision { get; }
}

public static class SingleWindowDraftConcurrency
{
    public static int ValidateExpectedRevision<TDocument>(
        TDocument? current,
        int sourceInvoiceId,
        int? expectedRevision)
        where TDocument : class
    {
        int currentRevision = current switch
        {
            Models.Entities.CustomsCooDocument coo => NormalizeCurrent(coo.DraftRevision),
            Models.Entities.AgentConsignmentDocument acd => NormalizeCurrent(acd.DraftRevision),
            _ => throw new ArgumentOutOfRangeException(nameof(current))
        };

        if (expectedRevision.HasValue && expectedRevision.Value != currentRevision)
        {
            throw new SingleWindowDraftConcurrencyException(
                "单证草稿已被其他用户修改，请刷新后合并或重新保存。",
                sourceInvoiceId,
                currentRevision);
        }

        return currentRevision;
    }

    public static void ValidateNewExpectedRevision(int sourceInvoiceId, int? expectedRevision)
    {
        if (expectedRevision.HasValue && expectedRevision.Value != 0)
        {
            throw new SingleWindowDraftConcurrencyException(
                "单证草稿已在其他用户处创建，请刷新后继续。",
                sourceInvoiceId,
                1);
        }
    }

    private static int NormalizeCurrent(int revision) => Math.Max(0, revision);
}
