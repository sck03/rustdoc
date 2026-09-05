namespace ExportDocManager.Models.Entities;

public static class SupplierStatusCatalog
{
    public const string Active = "合作中";
    public const string Evaluating = "考察中";
    public const string Paused = "暂停";
    public const string Inactive = "停用";

    public static readonly IReadOnlyList<string> Values = [Active, Evaluating, Paused, Inactive];

    public static bool IsKnown(string? value) =>
        Values.Contains(value?.Trim() ?? string.Empty, StringComparer.Ordinal);

    public static string Normalize(string? value, string fallback = Active)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) return fallback;
        return Values.FirstOrDefault(item => string.Equals(item, normalized, StringComparison.Ordinal))
            ?? throw new ArgumentException("供应商状态无效。");
    }
}

public static class SupplierProductLinkStatusCatalog
{
    public const string Active = "供货中";
    public const string Candidate = "备选";
    public const string Paused = "暂停";
    public const string Inactive = "停用";

    public static readonly IReadOnlyList<string> Values = [Active, Candidate, Paused, Inactive];

    public static bool IsKnown(string? value) =>
        Values.Contains(value?.Trim() ?? string.Empty, StringComparer.Ordinal);
}

public static class SupplierAssessmentCatalog
{
    public const string Periodic = "定期评价";
    public const string OrderReview = "订单复盘";
    public const string SampleEvaluation = "样品评估";
    public const string Other = "其它";
    public static readonly IReadOnlyList<string> Kinds = [Periodic, OrderReview, SampleEvaluation, Other];

    public const string Preferred = "优先合作";
    public const string Qualified = "合格";
    public const string Watch = "观察";
    public const string Paused = "暂停合作";
    public static readonly IReadOnlyList<string> Conclusions = [Preferred, Qualified, Watch, Paused];
}
