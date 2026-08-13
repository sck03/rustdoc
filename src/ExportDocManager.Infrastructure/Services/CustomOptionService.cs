using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class CustomOptionService : ICustomOptionService
    {
        private const int MaximumOptionsPerType = 500;
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        public CustomOptionService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<IReadOnlyList<string>> GetOptionsAsync(
            string optionType,
            CancellationToken cancellationToken = default)
        {
            var normalizedType = TextSearchHelper.NormalizeValue(optionType);
            if (string.IsNullOrWhiteSpace(normalizedType))
            {
                return Array.Empty<string>();
            }

            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var values = await context.CustomOptions
                .AsNoTracking()
                .Where(option => option.OptionType == normalizedType)
                .OrderByDescending(option => option.CreatedAt)
                .ThenByDescending(option => option.Id)
                .Take(MaximumOptionsPerType)
                .Select(option => option.OptionValue)
                .ToListAsync(cancellationToken);
            values.Reverse();
            return values
                .Select(TextSearchHelper.NormalizeValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task SaveOptionAsync(
            string optionType,
            string optionValue,
            CancellationToken cancellationToken = default)
        {
            var normalizedType = TextSearchHelper.NormalizeValue(optionType);
            var normalizedValue = TextSearchHelper.NormalizeValue(optionValue);
            if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedValue))
            {
                return;
            }

            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            string comparisonValue = normalizedValue.ToUpperInvariant();
            bool exists = await context.CustomOptions
                .AsNoTracking()
                .AnyAsync(
                    option => option.OptionType == normalizedType &&
                        option.OptionValue.ToUpper() == comparisonValue,
                    cancellationToken);

            if (exists)
            {
                return;
            }

            await context.CustomOptions.AddAsync(new CustomOption
            {
                OptionType = normalizedType,
                OptionValue = normalizedValue,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
