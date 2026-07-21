using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.MasterData;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiHsCodeDto> FromHsCodes(IEnumerable<HsCode> rows)
        {
            return rows?.Select(FromHsCode).ToList() ?? new List<ApiHsCodeDto>();
        }

        public static ApiPagedResponse<ApiHsCodeDto> FromPagedHsCodes(PagedResult<HsCode> result)
        {
            ArgumentNullException.ThrowIfNull(result);

            return new ApiPagedResponse<ApiHsCodeDto>(
                result.Items.Select(FromHsCode).ToList(),
                result.TotalCount,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage);
        }

        public static ApiHsCodeDto FromHsCode(HsCode hsCode)
        {
            ArgumentNullException.ThrowIfNull(hsCode);

            return new ApiHsCodeDto(
                hsCode.Id,
                hsCode.Code ?? string.Empty,
                hsCode.NormalizedCode ?? string.Empty,
                hsCode.Name ?? string.Empty,
                hsCode.Unit ?? string.Empty,
                hsCode.Description ?? string.Empty,
                hsCode.Elements ?? string.Empty,
                hsCode.SupervisionConditions ?? string.Empty,
                hsCode.InspectionCategory ?? string.Empty,
                hsCode.RebateRate ?? string.Empty,
                hsCode.UpdateTime,
                hsCode.DetailUrl ?? string.Empty,
                hsCode.Status ?? "Active",
                hsCode.SourceName ?? string.Empty,
                hsCode.EffectiveYear,
                hsCode.LastVerifiedAt,
                hsCode.ReplacedByCodes ?? string.Empty,
                hsCode.NormalTariffRate ?? string.Empty,
                hsCode.PreferentialTariffRate ?? string.Empty,
                hsCode.ExportTariffRate ?? string.Empty,
                hsCode.ConsumptionTaxRate ?? string.Empty,
                hsCode.ValueAddedTaxRate ?? string.Empty,
                hsCode.Notes ?? string.Empty);
        }

        public static ApiHsCodeDto FromRemoteRecord(HsCodeRemoteSearchRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            var dto = FromHsCode(record.Item);
            return dto with
            {
                RemoteRecordKind = record.Kind.ToString(),
                InstanceCount = record.InstanceCount,
                SummaryUrl = record.SummaryUrl ?? string.Empty,
                EvidenceUrl = record.EvidenceUrl ?? string.Empty,
                ObservedAt = record.ObservedAt.LocalDateTime
            };
        }

        public static ApiHsCodeDto FromRemoteDetail(HsCodeRemoteDetailBundle bundle)
        {
            ArgumentNullException.ThrowIfNull(bundle);
            var dto = FromHsCode(bundle.Item);
            return dto with
            {
                RemoteRecordKind = HsCodeRemoteRecordKind.StandardCode.ToString(),
                InstanceCount = bundle.InstanceCount,
                SummaryUrl = string.IsNullOrWhiteSpace(bundle.EvidenceUrl) ? bundle.Item.DetailUrl ?? string.Empty : bundle.EvidenceUrl,
                EvidenceUrl = bundle.EvidenceUrl ?? string.Empty,
                ObservedAt = bundle.ObservedAt.LocalDateTime,
                RecommendedKeywords = bundle.RecommendedKeywords.ToList(),
                PersonalPostalTaxCode = bundle.PersonalPostalTaxCode ?? string.Empty,
                CiqEntries = bundle.CiqEntries.Select(item => new ApiHsCodeRemoteReferenceEntry(item.Code, item.Name)).ToList(),
                ClassificationEntries = bundle.ClassificationEntries.Select(item => new ApiHsCodeRemoteReferenceEntry(item.Code, item.Name)).ToList(),
                DeclarationExampleCount = bundle.DeclarationExamples.Count
            };
        }

        public static HsCode ToHsCodeForSave(ApiHsCodeDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new HsCode
            {
                Id = dto.Id,
                Code = dto.Code ?? string.Empty,
                Name = dto.Name ?? string.Empty,
                Unit = dto.Unit ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                Elements = dto.Elements ?? string.Empty,
                SupervisionConditions = dto.SupervisionConditions ?? string.Empty,
                InspectionCategory = dto.InspectionCategory ?? string.Empty,
                RebateRate = dto.RebateRate ?? string.Empty,
                UpdateTime = dto.UpdateTime,
                DetailUrl = dto.DetailUrl ?? string.Empty,
                Status = dto.Status ?? "Active",
                SourceName = dto.SourceName ?? string.Empty,
                EffectiveYear = dto.EffectiveYear,
                LastVerifiedAt = dto.LastVerifiedAt,
                ReplacedByCodes = dto.ReplacedByCodes ?? string.Empty,
                NormalTariffRate = dto.NormalTariffRate ?? string.Empty,
                PreferentialTariffRate = dto.PreferentialTariffRate ?? string.Empty,
                ExportTariffRate = dto.ExportTariffRate ?? string.Empty,
                ConsumptionTaxRate = dto.ConsumptionTaxRate ?? string.Empty,
                ValueAddedTaxRate = dto.ValueAddedTaxRate ?? string.Empty,
                Notes = dto.Notes ?? string.Empty
            };
        }
    }
}
