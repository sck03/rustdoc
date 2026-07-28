namespace ExportDocManager.Services.Infrastructure
{
    public interface IAppPathProvider
    {
        string AppRoot { get; }
        string DataRoot { get; }
        string DatabaseRoot { get; }
        /// <summary>Bundled report templates shipped with the application. This location is read-only at runtime.</summary>
        string TemplateRoot { get; }
        /// <summary>User-created and imported report templates stored with runtime data.</summary>
        string UserTemplateRoot { get; }
        string ResourceRoot { get; }
        string BrowserRoot { get; }
        string ToolRoot { get; }
        string FileRoot { get; }
        string ExportRoot { get; }
        string BackupRoot { get; }
        string SingleWindowRoot { get; }
        string OcrModelRoot { get; }
        string LogRoot { get; }
        string CacheRoot { get; }
        string ConfigRoot { get; }
        string SecurityRoot { get; }
        string WebViewRoot { get; }
    }
}
