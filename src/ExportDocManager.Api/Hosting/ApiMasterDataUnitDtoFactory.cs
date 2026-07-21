using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiUnitDto> FromUnits(IEnumerable<Unit> rows)
        {
            return rows?.Select(FromUnit).ToList() ?? new List<ApiUnitDto>();
        }

        public static ApiUnitDto FromUnit(Unit unit)
        {
            ArgumentNullException.ThrowIfNull(unit);

            return new ApiUnitDto(
                unit.Id,
                unit.NameEN ?? string.Empty,
                unit.NameCN ?? string.Empty,
                unit.Code ?? string.Empty,
                RowVersionToString(unit.RowVersion));
        }

        public static Unit ToUnitForSave(ApiUnitDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Unit
            {
                Id = dto.Id,
                NameEN = dto.NameEN ?? string.Empty,
                NameCN = dto.NameCN ?? string.Empty,
                Code = dto.Code ?? string.Empty,
                RowVersion = RowVersionFromString(dto.RowVersion)
            };
        }
    }
}
