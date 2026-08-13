namespace ExportDocManager.Services.Infrastructure
{
    public interface IRuntimeDependencyDiagnosticContributor
    {
        IReadOnlyList<RuntimeDependencyDiagnostic> Inspect();
    }

    public interface IRuntimeDependencyDiagnosticsService
    {
        IReadOnlyList<RuntimeDependencyDiagnostic> Inspect();
    }

    public sealed record RuntimeDependencyDiagnostic(
        string Key,
        string Label,
        string Requirement,
        string Status,
        bool Ready,
        string ResolvedPath,
        string Message);
}
