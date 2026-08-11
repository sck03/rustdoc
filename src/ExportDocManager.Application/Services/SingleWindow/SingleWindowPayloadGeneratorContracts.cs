using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ICustomsCooPayloadGenerator
    {
        PayloadBuildResult BuildCertificateXml(CooMappedDocument document);

        Task WriteAttachmentXmlAsync(
            CooMappedDocument document,
            SingleWindowAttachmentSource attachment,
            Stream destination,
            CancellationToken cancellationToken = default);
    }

    public interface IAgentConsignmentPayloadGenerator
    {
        PayloadBuildResult BuildRequestXml(AcdMappedDocument document);
    }
}
