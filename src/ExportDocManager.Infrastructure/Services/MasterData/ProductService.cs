using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Errors;
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

        public async Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var rows = await _productReadRepository.QueryAsync(new ProductReadQuery(), cancellationToken);
            return rows.ToList();
        }

        public async Task<Product> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Products.FindAsync([id], cancellationToken);
        }

        public async Task<Product> GetByCodeAsync(
            string productCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedCode = TextSearchHelper.NormalizeValue(productCode);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return null;
            }

            var comparisonCode = normalizedCode.ToUpperInvariant();
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(product =>
                    product.ProductCode != null &&
                    product.ProductCode.ToUpper() == comparisonCode,
                    cancellationToken);
        }

        public async Task<int> AddProductAsync(
            Product product,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(product);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            NormalizeProduct(product);
            product.CreatedAt = DateTime.Now;
            product.UpdatedAt = DateTime.Now;
            context.Products.Add(product);
            await context.SaveChangesAsync(cancellationToken);
            return product.Id;
        }

        public async Task<bool> UpdateProductAsync(
            Product product,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(product);

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var existing = await context.Products.FindAsync([product.Id], cancellationToken);
                if (existing == null) return false;

                NormalizeProduct(product);
                context.Entry(existing).CurrentValues.SetValues(product);
                context.Entry(existing).Property(item => item.RowVersion).OriginalValue = product.RowVersion;
                existing.UpdatedAt = DateTime.Now;
                await context.SaveChangesAsync(cancellationToken);
                product.RowVersion = existing.RowVersion?.ToArray();
                return true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ServiceConcurrencyException("该商品已被其他用户修改，请加载最新数据后再保存。", ex);
            }
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var product = await context.Products.FindAsync([id], cancellationToken);
            if (product == null) return false;

            if (await context.SupplierProductLinks.AsNoTracking().AnyAsync(link => link.ProductId == id, cancellationToken))
            {
                throw new ResourceConflictException("该商品仍有关联的供应商供货资料，请先解除供应商供货关联后再删除。");
            }

            context.Products.Remove(product);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<List<Product>> SearchAsync(
            string keyword,
            CancellationToken cancellationToken = default)
        {
            var rows = await _productReadRepository.QueryAsync(new ProductReadQuery
            {
                Keyword = keyword ?? string.Empty
            }, cancellationToken);
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
