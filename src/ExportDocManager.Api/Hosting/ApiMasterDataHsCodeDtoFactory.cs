using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

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
                hsCode.DetailUrl ?? string.Empty);
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
                DetailUrl = dto.DetailUrl ?? string.Empty
            };
        }
    }
}
