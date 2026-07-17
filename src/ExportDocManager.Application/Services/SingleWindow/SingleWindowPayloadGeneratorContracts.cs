using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooPayloadGenerator
    {
        PayloadBuildResult BuildCertificateXml(CooMappedDocument document);

        IReadOnlyList<PayloadBuildResult> BuildAttachmentXmls(CooMappedDocument document);
    }

    public interface IAgentConsignmentPayloadGenerator
    {
        PayloadBuildResult BuildRequestXml(AcdMappedDocument document);
    }
}
