using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiExporterDto> FromExporters(IEnumerable<Exporter> rows)
        {
            return rows?.Select(FromExporter).ToList() ?? new List<ApiExporterDto>();
        }

        public static ApiExporterDto FromExporter(Exporter exporter)
        {
            ArgumentNullException.ThrowIfNull(exporter);

            return new ApiExporterDto(
                exporter.Id,
                exporter.ExporterNameEN ?? string.Empty,
                exporter.ExporterNameCN ?? string.Empty,
                exporter.AddressEN ?? string.Empty,
                exporter.AddressCN ?? string.Empty,
                exporter.ContactPerson ?? string.Empty,
                exporter.CreditCode ?? string.Empty,
                exporter.CustomsCode ?? string.Empty,
                exporter.Phone ?? string.Empty,
                exporter.BankName ?? string.Empty,
                exporter.BankAccount ?? string.Empty,
                exporter.SwiftCode ?? string.Empty,
                exporter.Notes ?? string.Empty,
                exporter.DocSealPath ?? string.Empty,
                exporter.CustomsSealPath ?? string.Empty,
                RowVersionToString(exporter.RowVersion));
        }

        public static Exporter ToExporterForSave(ApiExporterDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Exporter
            {
                Id = dto.Id,
                ExporterNameEN = dto.ExporterNameEN ?? string.Empty,
                ExporterNameCN = dto.ExporterNameCN ?? string.Empty,
                AddressEN = dto.AddressEN ?? string.Empty,
                AddressCN = dto.AddressCN ?? string.Empty,
                ContactPerson = dto.ContactPerson ?? string.Empty,
                CreditCode = dto.CreditCode ?? string.Empty,
                CustomsCode = dto.CustomsCode ?? string.Empty,
                Phone = dto.Phone ?? string.Empty,
                BankName = dto.BankName ?? string.Empty,
                BankAccount = dto.BankAccount ?? string.Empty,
                SwiftCode = dto.SwiftCode ?? string.Empty,
                Notes = dto.Notes ?? string.Empty,
                DocSealPath = dto.DocSealPath ?? string.Empty,
                CustomsSealPath = dto.CustomsSealPath ?? string.Empty,
                RowVersion = RowVersionFromString(dto.RowVersion)
            };
        }
    }
}
