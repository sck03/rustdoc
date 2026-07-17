using System.Threading;
using System.Threading.Tasks;

namespace ExportDocManager.Services.Tools
{
    public interface IAIService
    {
        Task<string> AnalyzeComplianceAsync(string prompt, string content, CancellationToken cancellationToken = default);
    }
}
