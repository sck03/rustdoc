using System.Collections.Frozen;

namespace ExportDocManager.Services.Infrastructure;

public sealed class RuntimeCapabilitySet(IEnumerable<string> keys)
{
    private readonly FrozenSet<string> _keys = keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public bool Contains(string key) => _keys.Contains(key);
    public IReadOnlyList<string> Keys => _keys.Order(StringComparer.Ordinal).ToArray();
}

public static class CapabilityModuleKeys
{
    public const string BusinessAttachments = "business-attachments";
    public const string Worklist = "worklist";
    public static readonly IReadOnlyList<string> All = ["excel", "browser", "pdf-ocr", BusinessAttachments, Worklist];
}
