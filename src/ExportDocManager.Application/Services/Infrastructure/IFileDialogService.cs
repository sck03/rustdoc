namespace ExportDocManager.Services.Infrastructure
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public interface IFileDialogService
    {
        string ShowOpenFile(FileDialogOptions options);

        IReadOnlyList<string> ShowOpenFiles(FileDialogOptions options);

        string ShowSaveFile(FileDialogOptions options);

        string ShowFolder(FileDialogOptions options);
    }
}
