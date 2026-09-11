using System.Globalization;
using System.Text;

namespace ExportDocManager.Models;

/// <summary>Shared display names; document values and template expressions retain their stable keys.</summary>
public sealed class DocumentFieldLabelSettings
{
    public const int FieldCount = 10;
    public const int MaximumLabelLength = 40;

    public Dictionary<string, string> Invoice { get; set; } = [];
    public Dictionary<string, string> Item { get; set; } = [];
    public Dictionary<string, string> Payment { get; set; } = [];

    public static DocumentFieldLabelSettings Normalize(DocumentFieldLabelSettings? value) => new()
    {
        Invoice = NormalizeGroup(value?.Invoice, "发票表头"),
        Item = NormalizeGroup(value?.Item, "商品明细"),
        Payment = NormalizeGroup(value?.Payment, "付款报销")
    };

    public static string GetLabel(IReadOnlyDictionary<string, string> labels, int index) =>
        labels.TryGetValue($"spare{index}", out var label) && !string.IsNullOrWhiteSpace(label)
            ? label : $"备用 {index}";

    private static Dictionary<string, string> NormalizeGroup(Dictionary<string, string>? source, string group)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is not null)
        {
            foreach (var (key, value) in source)
            {
                if (!Enumerable.Range(1, FieldCount).Any(index => key == $"spare{index}"))
                    throw new ArgumentException($"{group}包含无效备用字段：{key}。");
                var label = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
                if (label.Length > MaximumLabelLength || label.Any(character => char.IsControl(character)
                    || char.GetUnicodeCategory(character) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
                    throw new ArgumentException($"{group}的备用字段名称最多 {MaximumLabelLength} 字，不能包含换行或控制字符。");
                if (label.Length > 0) result.Add(key, label);
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index <= FieldCount; index++)
        {
            if (!names.Add(GetLabel(result, index)))
                throw new ArgumentException($"{group}的备用字段名称不能重复。");
        }
        return result;
    }
}
