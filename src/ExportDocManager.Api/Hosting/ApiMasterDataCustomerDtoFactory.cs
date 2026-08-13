using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiCustomerDto> FromCustomers(IEnumerable<Customer> rows)
        {
            return rows?.Select(FromCustomer).ToList() ?? new List<ApiCustomerDto>();
        }

        public static ApiCustomerDto FromCustomer(Customer customer)
        {
            ArgumentNullException.ThrowIfNull(customer);

            return new ApiCustomerDto(
                customer.Id,
                customer.CustomerNameEN ?? string.Empty,
                customer.DisplayName ?? string.Empty,
                customer.NotifyPartyMode == NotifyPartyMode.Separate ? customer.NotifyPartyName ?? string.Empty : string.Empty,
                customer.AddressEN ?? string.Empty,
                customer.NotifyPartyMode == NotifyPartyMode.Separate ? customer.NotifyPartyAddress ?? string.Empty : string.Empty,
                customer.ContactPerson ?? string.Empty,
                customer.Phone ?? string.Empty,
                customer.Email ?? string.Empty,
                customer.TaxId ?? string.Empty,
                customer.Notes ?? string.Empty,
                RowVersionToString(customer.RowVersion),
                customer.NotifyPartyMode);
        }

        public static Customer ToCustomerForSave(ApiCustomerDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Customer
            {
                Id = dto.Id,
                CustomerNameEN = dto.CustomerNameEN ?? string.Empty,
                NotifyPartyMode = dto.NotifyPartyMode,
                NotifyPartyName = dto.NotifyPartyMode == NotifyPartyMode.Separate ? dto.NotifyPartyName ?? string.Empty : string.Empty,
                AddressEN = dto.AddressEN ?? string.Empty,
                NotifyPartyAddress = dto.NotifyPartyMode == NotifyPartyMode.Separate ? dto.NotifyPartyAddress ?? string.Empty : string.Empty,
                ContactPerson = dto.ContactPerson ?? string.Empty,
                Phone = dto.Phone ?? string.Empty,
                Email = dto.Email ?? string.Empty,
                TaxId = dto.TaxId ?? string.Empty,
                Notes = dto.Notes ?? string.Empty,
                RowVersion = RowVersionFromString(dto.RowVersion)
            };
        }
    }
}
