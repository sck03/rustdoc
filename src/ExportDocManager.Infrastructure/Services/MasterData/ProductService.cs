using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public class ProductService : IProductService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IProductReadRepository _productReadRepository;

        public ProductService(
            IDbContextFactory<AppDbContext> contextFactory,
            IProductReadRepository productReadRepository)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _productReadRepository = productReadRepository ?? throw new ArgumentNullException(nameof(productReadRepository));
        }

        public async Task<List<Product>> GetAllAsync()
        {
            var rows = await _productReadRepository.QueryAsync(new ProductReadQuery());
            return rows.ToList();
        }

        public async Task<Product> GetByIdAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Products.FindAsync(id);
        }

        public async Task<Product> GetByCodeAsync(string productCode)
        {
            var normalizedCode = TextSearchHelper.NormalizeValue(productCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return null;
            }

            var comparisonCode = normalizedCode.ToUpperInvariant();
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(product =>
                    product.ProductCode != null &&
                    product.ProductCode.ToUpper() == comparisonCode);
        }

        public async Task<int> AddProductAsync(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);

            using var context = await _contextFactory.CreateDbContextAsync();
            NormalizeProduct(product);
            product.CreatedAt = DateTime.Now;
            product.UpdatedAt = DateTime.Now;
            context.Products.Add(product);
            await context.SaveChangesAsync();
            return product.Id;
        }

        public async Task<bool> UpdateProductAsync(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);

            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.Products.FindAsync(product.Id);
            if (existing == null) return false;

            NormalizeProduct(product);
            context.Entry(existing).CurrentValues.SetValues(product);
            existing.UpdatedAt = DateTime.Now;
            // Ensure Id didn't change (though SetValues usually handles this)
            
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products.FindAsync(id);
            if (product == null) return false;

            context.Products.Remove(product);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<List<Product>> SearchAsync(string keyword)
        {
            var rows = await _productReadRepository.QueryAsync(new ProductReadQuery
            {
                Keyword = keyword ?? string.Empty
            });
            return rows.ToList();
        }

        private static void NormalizeProduct(Product product)
        {
            product.ProductCode = TextSearchHelper.NormalizeValue(product.ProductCode);
            product.NameEN = TextSearchHelper.NormalizeValue(product.NameEN);
            product.NameCN = TextSearchHelper.NormalizeValue(product.NameCN);
            product.Description = TextSearchHelper.NormalizeValue(product.Description);
            product.HSCode = TextSearchHelper.NormalizeUpperValue(product.HSCode);
            product.Elements = TextSearchHelper.NormalizeValue(product.Elements);
            product.SupervisionConditions = TextSearchHelper.NormalizeValue(product.SupervisionConditions);
            product.InspectionCategory = TextSearchHelper.NormalizeValue(product.InspectionCategory);
            product.Material = TextSearchHelper.NormalizeValue(product.Material);
            product.Brand = TextSearchHelper.NormalizeValue(product.Brand);
            product.Origin = TextSearchHelper.NormalizeValue(product.Origin);
            product.UnitEN = TextSearchHelper.NormalizeUpperValue(product.UnitEN);
            product.UnitCN = TextSearchHelper.NormalizeValue(product.UnitCN);
            product.PackageUnitEN = TextSearchHelper.NormalizeUpperValue(product.PackageUnitEN);
            product.PackageUnitCN = TextSearchHelper.NormalizeValue(product.PackageUnitCN);
        }
    }
}
