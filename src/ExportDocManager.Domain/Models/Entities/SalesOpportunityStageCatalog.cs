namespace ExportDocManager.Models.Entities;

/// <summary>
/// Canonical sales pipeline stages and the only transitions allowed by the
/// application. Keeping the transition graph beside the domain values avoids
/// each client inventing a different interpretation of the pipeline.
/// </summary>
public static class SalesOpportunityStageCatalog
{
    public const string Lead = "线索";
    public const string Qualification = "需求确认";
    public const string Quoted = "已报价";
    public const string Negotiating = "谈判中";
    public const string Won = "已成交";
    public const string Lost = "已失单";

    public static IReadOnlyList<string> Values { get; } =
        [Lead, Qualification, Quoted, Negotiating, Won, Lost];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Transitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Lead] = Set(Qualification),
            [Qualification] = Set(Lead, Quoted),
            [Quoted] = Set(Qualification, Negotiating),
            [Negotiating] = Set(Quoted, Won, Lost),
            [Won] = Set(Negotiating),
            [Lost] = Set(Negotiating),
        };

    public static bool IsKnown(string? stage) =>
        Values.Contains(stage?.Trim() ?? string.Empty, StringComparer.Ordinal);

    public static string Normalize(string? stage)
    {
        string value = stage?.Trim() ?? string.Empty;
        return IsKnown(value) ? value : throw new ArgumentException("商机阶段无效。");
    }

    public static bool CanTransition(string? from, string? to)
    {
        string source = from?.Trim() ?? string.Empty;
        string target = to?.Trim() ?? string.Empty;
        return string.Equals(source, target, StringComparison.Ordinal) ||
               Transitions.TryGetValue(source, out var targets) && targets.Contains(target);
    }

    public static IReadOnlyList<string> GetAllowedTransitions(string? from)
    {
        string source = from?.Trim() ?? string.Empty;
        return Transitions.TryGetValue(source, out var targets)
            ? Values.Where(targets.Contains).ToArray()
            : [];
    }

    public static bool IsClosed(string? stage) =>
        string.Equals(stage?.Trim(), Won, StringComparison.Ordinal) ||
        string.Equals(stage?.Trim(), Lost, StringComparison.Ordinal);

    private static IReadOnlySet<string> Set(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);
}
