using System.Threading;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public interface IContainerPackingEngine
    {
        ContainerPackingAnalysis Analyze(ContainerPackingRequest request, CancellationToken cancellationToken = default);
    }
}
