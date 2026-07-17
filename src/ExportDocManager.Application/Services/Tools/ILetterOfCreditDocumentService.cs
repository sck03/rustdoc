using System.Threading;

namespace ExportDocManager.Services.Tools
{
    public sealed class LetterOfCreditDocumentImportResult
    {
        public string SourcePath { get; init; } = string.Empty;

        public string ExtractedText { get; init; } = string.Empty;

        public string SourceDescription { get; init; } = string.Empty;
    }

    public interface ILetterOfCreditDocumentService
    {
        Task<LetterOfCreditDocumentImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
