namespace ExportDocManager.Services.Infrastructure
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public interface ITextPromptService
    {
        string ShowPrompt(TextPromptOptions options);
    }
}
