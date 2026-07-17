using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class CustomOptionService : ICustomOptionService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        public CustomOptionService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public IReadOnlyList<string> GetOptions(string optionType)
        {
            var normalizedType = TextSearchHelper.NormalizeValue(optionType);
            if (string.IsNullOrWhiteSpace(normalizedType))
            {
                return Array.Empty<string>();
            }

            using var context = _dbContextFactory.CreateDbContext();
            return context.CustomOptions
                .AsNoTracking()
                .Where(option => option.OptionType == normalizedType)
                .OrderBy(option => option.CreatedDate)
                .Select(option => option.OptionValue)
                .AsEnumerable()
                .Select(TextSearchHelper.NormalizeValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void SaveOption(string optionType, string optionValue)
        {
            var normalizedType = TextSearchHelper.NormalizeValue(optionType);
            var normalizedValue = TextSearchHelper.NormalizeValue(optionValue);
            if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedValue))
            {
                return;
            }

            using var context = _dbContextFactory.CreateDbContext();
            var exists = context.CustomOptions
                .AsNoTracking()
                .Where(option => option.OptionType == normalizedType)
                .Select(option => option.OptionValue)
                .AsEnumerable()
                .Any(value => string.Equals(TextSearchHelper.NormalizeValue(value), normalizedValue, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return;
            }

            context.CustomOptions.Add(new CustomOption
            {
                OptionType = normalizedType,
                OptionValue = normalizedValue,
                CreatedDate = DateTime.Now
            });
            context.SaveChanges();
        }
    }
}
