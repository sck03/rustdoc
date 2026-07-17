using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiMasterDataDtoFactory
    {
        public static IReadOnlyList<ApiProductDto> FromProducts(IEnumerable<Product> rows)
        {
            return rows?.Select(FromProduct).ToList() ?? new List<ApiProductDto>();
        }

        public static ApiProductDto FromProduct(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);

            return new ApiProductDto(
                product.Id,
                product.ProductCode ?? string.Empty,
                product.NameEN ?? string.Empty,
                product.NameCN ?? string.Empty,
                product.Description ?? string.Empty,
                product.HSCode ?? string.Empty,
                product.Elements ?? string.Empty,
                product.SupervisionConditions ?? string.Empty,
                product.InspectionCategory ?? string.Empty,
                product.TaxRebateRate,
                product.Material ?? string.Empty,
                product.Brand ?? string.Empty,
                product.Origin ?? string.Empty,
                product.UnitEN ?? string.Empty,
                product.UnitCN ?? string.Empty,
                product.Length,
                product.Width,
                product.Height,
                product.GWPerCtn,
                product.NWPerCtn,
                product.PcsPerCtn,
                product.PackageUnitEN ?? string.Empty,
                product.PackageUnitCN ?? string.Empty,
                product.DefaultPrice,
                product.CreatedAt,
                product.UpdatedAt);
        }

        public static Product ToProductForSave(ApiProductDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);

            return new Product
            {
                Id = dto.Id,
                ProductCode = dto.ProductCode ?? string.Empty,
                NameEN = dto.NameEN ?? string.Empty,
                NameCN = dto.NameCN ?? string.Empty,
                Description = dto.Description ?? string.Empty,
                HSCode = dto.HSCode ?? string.Empty,
                Elements = dto.Elements ?? string.Empty,
                SupervisionConditions = dto.SupervisionConditions ?? string.Empty,
                InspectionCategory = dto.InspectionCategory ?? string.Empty,
                TaxRebateRate = dto.TaxRebateRate,
                Material = dto.Material ?? string.Empty,
                Brand = dto.Brand ?? string.Empty,
                Origin = dto.Origin ?? string.Empty,
                UnitEN = dto.UnitEN ?? string.Empty,
                UnitCN = dto.UnitCN ?? string.Empty,
                Length = dto.Length,
                Width = dto.Width,
                Height = dto.Height,
                GWPerCtn = dto.GWPerCtn,
                NWPerCtn = dto.NWPerCtn,
                PcsPerCtn = dto.PcsPerCtn,
                PackageUnitEN = dto.PackageUnitEN ?? string.Empty,
                PackageUnitCN = dto.PackageUnitCN ?? string.Empty,
                DefaultPrice = dto.DefaultPrice,
                CreatedAt = dto.CreatedAt,
                UpdatedAt = dto.UpdatedAt
            };
        }
    }
}
