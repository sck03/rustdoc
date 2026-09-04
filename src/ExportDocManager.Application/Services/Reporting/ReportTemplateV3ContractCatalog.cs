namespace ExportDocManager.Services.Reporting;

public static class ReportTemplateV3ContractCatalog
{
    public const int SchemaVersion = 3;
    public const string ContractVersion = "3.0";
    public const string AstKind = "ReportDocument";
    public const string CoordinateUnit = "hundredth-mm";
    public const string PageSize = "A4";
    public const string RepeatBandSource = "layers[].print + DetailTable.print";
    public const string DetailTableRepeatSemantics = "repeatHeaderOnPageBreak";
    public const string ImageResourcePolicy = "managed-resource-or-controlled-field";
    public const bool AllowsDynamicEvaluation = false;
    public static IReadOnlyList<string> Orientations { get; } = ["Portrait", "Landscape"];
    public static IReadOnlyList<string> LayerRoles { get; } = ["Header", "Body", "Footer", "Overlay"];
    public static IReadOnlyList<string> RepeatBandLayerRoles { get; } = ["Header", "Footer"];
    public static IReadOnlyList<string> ElementTypes { get; } = ["Text", "Field", "Line", "Rectangle", "Image", "PageNumber", "Flow"];
    public static IReadOnlyList<string> FlowTypes { get; } = ["Row", "Grid", "Conditional", "DetailTable", "PageBreak"];
    public static IReadOnlyList<string> InlineBlockTypes { get; } = ["Text", "Field", "Image", "PageBreak"];
    public static IReadOnlyList<string> ConditionOperators { get; } = ["HasValue", "Equals", "NotEquals"];
    public static IReadOnlyList<string> AggregateOperators { get; } = ["Sum", "Count"];
    public static IReadOnlyList<string> ReleaseStates { get; } = ["Draft", "Published", "Archived"];
    public static IReadOnlyList<string> ReportTypes { get; } = ["ExportDocument", "PaymentVoucher"];
    public static IReadOnlyList<string> ImageMediaTypes { get; } = ["image/png", "image/jpeg", "image/gif", "image/webp"];
    public static IReadOnlyList<string> ControlledImageFieldPaths { get; } = ["doc_seal_path", "customs_seal_path", "shipping_marks_image_data"];
    public static IReadOnlyList<string> StampFieldPaths { get; } = ["doc_seal_path", "customs_seal_path"];
    public const int MaxLayers = 16;
    public const int MaxElementsPerLayer = 1000;
    public const int MaxTotalElements = 4000;
    public const int MaxResources = 1000;
    public const int MaxResourceBytes = 32 * 1024 * 1024;
    public const int MinElementSize = 400;
    public const int MaxTextLength = 32768;
    public static (int WidthHundredthMm, int HeightHundredthMm) A4Dimensions(string orientation) =>
        string.Equals(orientation, "Landscape", StringComparison.Ordinal)
            ? (29700, 21000)
            : (21000, 29700);
}
