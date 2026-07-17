using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiPortDto> FromPorts(IEnumerable<Port> rows)
        {
            return rows?.Select(FromPort).ToList() ?? new List<ApiPortDto>();
        }

        public static ApiPortDto FromPort(Port port)
        {
            ArgumentNullException.ThrowIfNull(port);

            return new ApiPortDto(
                port.Id,
                port.NameEN ?? string.Empty,
                port.NameCN ?? string.Empty,
                port.Country ?? string.Empty,
                port.Code ?? string.Empty);
        }

        public static Port ToPortForSave(ApiPortDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Port
            {
                Id = dto.Id,
                NameEN = dto.NameEN ?? string.Empty,
                NameCN = dto.NameCN ?? string.Empty,
                Country = dto.Country ?? string.Empty,
                Code = dto.Code ?? string.Empty
            };
        }
    }
}
