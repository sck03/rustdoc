using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiSingleWindowDtoFactory
    {
        public static ApiCustomsCooDocumentDto FromCustomsCooDocument(CustomsCooDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            var dto = new ApiCustomsCooDocumentDto();
            CopyScalarProperties(document, dto);
            dto.Items = (document.Items ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.GNo)
                .Select(FromCustomsCooItem)
                .ToList();
            dto.NonpartyCorps = (document.NonpartyCorps ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.SortNo)
                .Select(FromCustomsCooNonpartyCorp)
                .ToList();
            dto.Attachments = (document.Attachments ?? [])
                .Where(item => item != null)
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.FileName)
                .Select(FromCustomsCooAttachment)
                .ToList();
            return dto;
        }

        public static CustomsCooDocument ToCustomsCooDocument(ApiCustomsCooDocumentDto dto, int sourceInvoiceId)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var document = new CustomsCooDocument();
            CopyScalarProperties(dto, document);
            document.SourceInvoiceId = sourceInvoiceId;
            document.Items = (dto.Items ?? Array.Empty<ApiCustomsCooItemDto>())
                .Where(item => item != null)
                .Select(ToCustomsCooItem)
                .ToList();
            document.NonpartyCorps = (dto.NonpartyCorps ?? Array.Empty<ApiCustomsCooNonpartyCorpDto>())
                .Where(item => item != null)
                .Select(ToCustomsCooNonpartyCorp)
                .ToList();
            document.Attachments = (dto.Attachments ?? Array.Empty<ApiCustomsCooAttachmentDto>())
                .Where(item => item != null)
                .Select(ToCustomsCooAttachment)
                .ToList();
            return document;
        }

        private static ApiCustomsCooItemDto FromCustomsCooItem(CustomsCooItem item)
        {
            var dto = new ApiCustomsCooItemDto();
            CopyScalarProperties(item, dto);
            return dto;
        }

        private static CustomsCooItem ToCustomsCooItem(ApiCustomsCooItemDto dto)
        {
            var item = new CustomsCooItem();
            CopyScalarProperties(dto, item);
            return item;
        }

        private static ApiCustomsCooNonpartyCorpDto FromCustomsCooNonpartyCorp(
            CustomsCooNonpartyCorp row)
        {
            var dto = new ApiCustomsCooNonpartyCorpDto();
            CopyScalarProperties(row, dto);
            return dto;
        }

        private static CustomsCooNonpartyCorp ToCustomsCooNonpartyCorp(
            ApiCustomsCooNonpartyCorpDto dto)
        {
            var row = new CustomsCooNonpartyCorp();
            CopyScalarProperties(dto, row);
            return row;
        }

        private static ApiCustomsCooAttachmentDto FromCustomsCooAttachment(CustomsCooAttachment row)
        {
            var dto = new ApiCustomsCooAttachmentDto();
            CopyScalarProperties(row, dto);
            return dto;
        }

        private static CustomsCooAttachment ToCustomsCooAttachment(ApiCustomsCooAttachmentDto dto)
        {
            var row = new CustomsCooAttachment();
            CopyScalarProperties(dto, row);
            return row;
        }
    }
}
