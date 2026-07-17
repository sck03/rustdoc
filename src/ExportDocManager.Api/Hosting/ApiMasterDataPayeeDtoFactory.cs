using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiPayeeDto> FromPayees(IEnumerable<Payee> rows)
        {
            return rows?.Select(FromPayee).ToList() ?? new List<ApiPayeeDto>();
        }

        public static ApiPayeeDto FromPayee(Payee payee)
        {
            ArgumentNullException.ThrowIfNull(payee);

            return new ApiPayeeDto(
                payee.Id,
                payee.Category ?? string.Empty,
                payee.Name ?? string.Empty,
                payee.BankName ?? string.Empty,
                payee.RMBAccount ?? string.Empty,
                payee.USDAccount ?? string.Empty,
                payee.ContactPerson ?? string.Empty,
                payee.Phone ?? string.Empty,
                payee.Notes ?? string.Empty);
        }

        public static Payee ToPayeeForSave(ApiPayeeDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Payee
            {
                Id = dto.Id,
                Category = dto.Category ?? string.Empty,
                Name = dto.Name ?? string.Empty,
                BankName = dto.BankName ?? string.Empty,
                RMBAccount = dto.RMBAccount ?? string.Empty,
                USDAccount = dto.USDAccount ?? string.Empty,
                ContactPerson = dto.ContactPerson ?? string.Empty,
                Phone = dto.Phone ?? string.Empty,
                Notes = dto.Notes ?? string.Empty
            };
        }
    }
}
