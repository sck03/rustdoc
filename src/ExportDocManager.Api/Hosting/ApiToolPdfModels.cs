namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiPdfMergeRequest
    {
        public List<string> SourceFiles { get; set; } = new();

        public string DestinationPath { get; set; } = string.Empty;
    }
}
