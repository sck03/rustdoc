namespace ExportDocManager.Models.Entities;

public static class ShippingMarksTypeCatalog
{
    public const string Text = "Text";
    public const string Image = "Image";

    public static string Normalize(string? value)
    {
        string type = value?.Trim() ?? string.Empty;
        if (type.Length == 0 || string.Equals(type, Text, StringComparison.OrdinalIgnoreCase)) return Text;
        return string.Equals(type, Image, StringComparison.OrdinalIgnoreCase) ? Image : type;
    }

    public static bool IsKnown(string? value) => Normalize(value) is Text or Image;
}
