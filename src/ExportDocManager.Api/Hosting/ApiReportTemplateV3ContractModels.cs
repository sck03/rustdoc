using ExportDocManager.Services.Reporting;

namespace ExportDocManager.Api.Hosting;

public sealed class ApiReportTemplateV3ContractResponse
{
    public string ContractVersion { get; init; } = ReportTemplateV3ContractCatalog.ContractVersion; public int SchemaVersion { get; init; } = ReportTemplateV3ContractCatalog.SchemaVersion;
    public string AstKind { get; init; } = ReportTemplateV3ContractCatalog.AstKind; public string CoordinateUnit { get; init; } = ReportTemplateV3ContractCatalog.CoordinateUnit;
    public ApiReportTemplateV3PageContract Page { get; init; } = new(); public IReadOnlyList<string> LayerRoles { get; init; } = ReportTemplateV3ContractCatalog.LayerRoles;
    public ApiReportTemplateV3RepeatBandContract RepeatBand { get; init; } = new(); public IReadOnlyList<string> ElementTypes { get; init; } = ReportTemplateV3ContractCatalog.ElementTypes;
    public IReadOnlyList<string> FlowTypes { get; init; } = ReportTemplateV3ContractCatalog.FlowTypes; public ApiReportTemplateV3ExpressionContract Expressions { get; init; } = new();
    public ApiReportTemplateV3ImageResourceContract Images { get; init; } = new(); public ApiReportTemplateV3ReleaseContract Release { get; init; } = new();
    public ApiReportTemplateV3LimitsContract Limits { get; init; } = new(); public IReadOnlyList<string> ReportTypes { get; init; } = ReportTemplateV3ContractCatalog.ReportTypes;
    public static ApiReportTemplateV3ContractResponse Create() => new();
}
public sealed class ApiReportTemplateV3PageContract
{
    public string Size { get; init; } = ReportTemplateV3ContractCatalog.PageSize; public IReadOnlyList<string> Orientations { get; init; } = ReportTemplateV3ContractCatalog.Orientations;
    public string CoordinateUnit { get; init; } = ReportTemplateV3ContractCatalog.CoordinateUnit; public IReadOnlyList<string> PageAndLayerRoles { get; init; } = ReportTemplateV3ContractCatalog.LayerRoles;
}
public sealed class ApiReportTemplateV3RepeatBandContract
{
    public string Source { get; init; } = ReportTemplateV3ContractCatalog.RepeatBandSource; public IReadOnlyList<string> LayerRoles { get; init; } = ReportTemplateV3ContractCatalog.RepeatBandLayerRoles;
    public string DetailTableSemantics { get; init; } = ReportTemplateV3ContractCatalog.DetailTableRepeatSemantics;
    public bool PersistedAsSeparateList { get; init; } = false;
}
public sealed class ApiReportTemplateV3ExpressionContract
{
    public IReadOnlyList<string> ConditionOperators { get; init; } = ReportTemplateV3ContractCatalog.ConditionOperators; public IReadOnlyList<string> AggregateOperators { get; init; } = ReportTemplateV3ContractCatalog.AggregateOperators;
    public bool AllowsDynamicEvaluation { get; init; } = ReportTemplateV3ContractCatalog.AllowsDynamicEvaluation;
}
public sealed class ApiReportTemplateV3ImageResourceContract
{
    public string Policy { get; init; } = ReportTemplateV3ContractCatalog.ImageResourcePolicy; public IReadOnlyList<string> MediaTypes { get; init; } = ReportTemplateV3ContractCatalog.ImageMediaTypes;
    public IReadOnlyList<string> ControlledFieldPaths { get; init; } = ReportTemplateV3ContractCatalog.ControlledImageFieldPaths;
    public bool AllowsExternalUrl { get; init; } = false;
}
public sealed class ApiReportTemplateV3ReleaseContract
{
    public IReadOnlyList<string> States { get; init; } = ReportTemplateV3ContractCatalog.ReleaseStates; public string RevisionType { get; init; } = "non-negative integer";
    public string PublishedAtType { get; init; } = "ISO-8601 DateTimeOffset";
}
public sealed class ApiReportTemplateV3LimitsContract
{
    public int MaxLayers { get; init; } = ReportTemplateV3ContractCatalog.MaxLayers; public int MaxElementsPerLayer { get; init; } = ReportTemplateV3ContractCatalog.MaxElementsPerLayer;
    public int MaxTotalElements { get; init; } = ReportTemplateV3ContractCatalog.MaxTotalElements; public int MaxResources { get; init; } = ReportTemplateV3ContractCatalog.MaxResources;
    public int MaxResourceBytes { get; init; } = ReportTemplateV3ContractCatalog.MaxResourceBytes; public int MinElementSizeHundredthMm { get; init; } = ReportTemplateV3ContractCatalog.MinElementSize;
}
