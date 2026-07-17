namespace ExportDocManager.Services.Infrastructure
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public interface IClipboardService
    {
        void SetText(string text);
    }
}
