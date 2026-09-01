using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public static ApiAgentConsignmentDocumentDto FromAgentConsignmentDocument(
            AgentConsignmentDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var dto = new ApiAgentConsignmentDocumentDto();
            CopyScalarProperties(document, dto);
            dto.ExpectedDraftRevision = document.DraftRevision;
            return dto;
        }

        public static AgentConsignmentDocument ToAgentConsignmentDocument(
            ApiAgentConsignmentDocumentDto dto,
            int sourceInvoiceId)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var document = new AgentConsignmentDocument();
            CopyScalarProperties(dto, document);
            document.SourceInvoiceId = sourceInvoiceId;
            document.ExpectedDraftRevision = dto.ExpectedDraftRevision;
            return document;
        }
    }
}
