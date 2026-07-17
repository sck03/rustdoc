using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooFieldMapper
    {
        CooMappedDocument Map(CooSourceSnapshot snapshot);
    }

    public interface IAgentConsignmentFieldMapper
    {
        AcdMappedDocument Map(AcdSourceSnapshot snapshot);
    }
}
