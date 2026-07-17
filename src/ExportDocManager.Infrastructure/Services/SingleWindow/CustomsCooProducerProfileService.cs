using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class CustomsCooProducerProfileService : ICustomsCooProducerProfileService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public CustomsCooProducerProfileService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<CustomsCooProducerProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await context.CustomsCooProducerProfiles
                .AsNoTracking()
                .OrderByDescending(item => item.LastUsedAt)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.CiqRegNo)
                .ThenBy(item => item.PrdcEtpsName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CustomsCooProducerProfile>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var query = context.CustomsCooProducerProfiles.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.ApplyKeywordSearch(
                    keyword,
                    item => item.CiqRegNo,
                    item => item.PrdcEtpsName,
                    item => item.PrdcEtpsConcEr,
                    item => item.PrdcEtpsTel,
                    item => item.Producer,
                    item => item.ProducerTel,
                    item => item.ProducerEmail,
                    item => item.LastInvoiceNo,
                    item => item.LastSourceStyleNo);
            }

            return await query
                .OrderByDescending(item => item.LastUsedAt)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.CiqRegNo)
                .ThenBy(item => item.PrdcEtpsName)
                .Take(300)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<CustomsCooProducerProfile> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await context.CustomsCooProducerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<CustomsCooProducerProfile> SaveOrUpdateAsync(CustomsCooProducerProfileInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var normalized = NormalizeInput(input);
            var existing = await FindExistingAsync(context, normalized, cancellationToken).ConfigureAwait(false);
            var now = DateTime.Now;

            if (existing == null)
            {
                existing = new CustomsCooProducerProfile
                {
                    CreatedAt = now
                };
                ApplyValues(existing, normalized, now);
                await context.CustomsCooProducerProfiles.AddAsync(existing, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ApplyValues(existing, normalized, now);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        public async Task<int> SaveAsync(CustomsCooProducerProfileInput input, int? profileId = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var normalized = NormalizeInput(input);
            var now = DateTime.Now;

            CustomsCooProducerProfile entity = null;
            if (profileId.GetValueOrDefault() > 0)
            {
                entity = await context.CustomsCooProducerProfiles
                    .FirstOrDefaultAsync(item => item.Id == profileId.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            entity ??= await FindExistingAsync(context, normalized, cancellationToken).ConfigureAwait(false);

            if (entity == null)
            {
                entity = new CustomsCooProducerProfile
                {
                    CreatedAt = now
                };
                ApplyValues(entity, normalized, now);
                entity.LastUsedAt = now;
                await context.CustomsCooProducerProfiles.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var previousLastUsedAt = entity.LastUsedAt;
                ApplyValues(entity, normalized, now);
                entity.LastUsedAt = previousLastUsedAt == default ? now : previousLastUsedAt;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return entity.Id;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var entity = await context.CustomsCooProducerProfiles
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
                .ConfigureAwait(false);
            if (entity == null)
            {
                return false;
            }

            context.CustomsCooProducerProfiles.Remove(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async Task<int> RememberProfilesAsync(IEnumerable<CustomsCooProducerProfileInput> inputs, CancellationToken cancellationToken = default)
        {
            var normalizedInputs = (inputs ?? Enumerable.Empty<CustomsCooProducerProfileInput>())
                .Where(item => item != null)
                .Select(NormalizeInput)
                .Where(HasUsableIdentity)
                .GroupBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (normalizedInputs.Count == 0)
            {
                return 0;
            }

            int affected = 0;
            foreach (var input in normalizedInputs)
            {
                await SaveOrUpdateAsync(input, cancellationToken).ConfigureAwait(false);
                affected++;
            }

            return affected;
        }

        private static async Task<CustomsCooProducerProfile> FindExistingAsync(
            AppDbContext context,
            CustomsCooProducerProfileInput input,
            CancellationToken cancellationToken)
        {
            string ciqRegNo = NormalizeText(input.CiqRegNo);
            string enterpriseName = NormalizeText(input.PrdcEtpsName);

            if (!string.IsNullOrWhiteSpace(ciqRegNo))
            {
                var byCode = await context.CustomsCooProducerProfiles
                    .FirstOrDefaultAsync(
                        item => item.CiqRegNo != null && item.CiqRegNo.ToUpper() == ciqRegNo.ToUpper(),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (byCode != null)
                {
                    return byCode;
                }
            }

            if (!string.IsNullOrWhiteSpace(enterpriseName))
            {
                return await context.CustomsCooProducerProfiles
                    .FirstOrDefaultAsync(
                        item => item.PrdcEtpsName != null && item.PrdcEtpsName.ToUpper() == enterpriseName.ToUpper(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return null;
        }

        private static void ApplyValues(CustomsCooProducerProfile target, CustomsCooProducerProfileInput source, DateTime now)
        {
            target.CiqRegNo = NormalizeUpperValue(source.CiqRegNo);
            target.PrdcEtpsName = NormalizeText(source.PrdcEtpsName);
            target.PrdcEtpsConcEr = NormalizeText(source.PrdcEtpsConcEr);
            target.PrdcEtpsTel = NormalizeText(source.PrdcEtpsTel);
            target.Producer = NormalizeText(source.Producer);
            target.ProducerTel = NormalizeText(source.ProducerTel);
            target.ProducerFax = NormalizeText(source.ProducerFax);
            target.ProducerEmail = NormalizeText(source.ProducerEmail);
            target.ProducerSertFlag = NormalizeUpperValue(source.ProducerSertFlag);
            target.LastInvoiceNo = NormalizeText(source.LastInvoiceNo);
            target.LastContractNo = NormalizeText(source.LastContractNo);
            target.LastSourceStyleNo = NormalizeText(source.LastSourceStyleNo);
            target.UpdatedAt = now;
            target.LastUsedAt = now;
        }

        private static CustomsCooProducerProfileInput NormalizeInput(CustomsCooProducerProfileInput input)
        {
            return new CustomsCooProducerProfileInput
            {
                CiqRegNo = NormalizeUpperValue(input?.CiqRegNo),
                PrdcEtpsName = NormalizeText(input?.PrdcEtpsName),
                PrdcEtpsConcEr = NormalizeText(input?.PrdcEtpsConcEr),
                PrdcEtpsTel = NormalizeText(input?.PrdcEtpsTel),
                Producer = NormalizeText(input?.Producer),
                ProducerTel = NormalizeText(input?.ProducerTel),
                ProducerFax = NormalizeText(input?.ProducerFax),
                ProducerEmail = NormalizeText(input?.ProducerEmail),
                ProducerSertFlag = NormalizeUpperValue(input?.ProducerSertFlag),
                LastInvoiceNo = NormalizeText(input?.LastInvoiceNo),
                LastContractNo = NormalizeText(input?.LastContractNo),
                LastSourceStyleNo = NormalizeText(input?.LastSourceStyleNo)
            };
        }

        private static bool HasUsableIdentity(CustomsCooProducerProfileInput input)
        {
            return !string.IsNullOrWhiteSpace(input?.CiqRegNo) ||
                   !string.IsNullOrWhiteSpace(input?.PrdcEtpsName);
        }

        private static string BuildIdentityKey(CustomsCooProducerProfileInput input)
        {
            if (!string.IsNullOrWhiteSpace(input?.CiqRegNo))
            {
                return "CODE:" + NormalizeUpperValue(input.CiqRegNo);
            }

            return "NAME:" + NormalizeText(input?.PrdcEtpsName).ToUpperInvariant();
        }

        private static string NormalizeText(string value)
        {
            return TextSearchHelper.NormalizeValue(value);
        }

        private static string NormalizeUpperValue(string value)
        {
            return TextSearchHelper.NormalizeUpperValue(value);
        }
    }
}
