using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class CustomsCooProducerProfileService : ICustomsCooProducerProfileService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IBusinessClock _clock;

        public CustomsCooProducerProfileService(
            IDbContextFactory<AppDbContext> contextFactory,
            IBusinessClock? clock = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _clock = clock ?? BusinessClock.CreateSystem();
        }

        public async Task<IReadOnlyList<CustomsCooProducerProfile>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var query = context.CustomsCooProducerProfiles.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.ApplyKeywordSearch(
                    context,
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

        public async Task<CustomsCooProducerProfile?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
            var now = _clock.UtcNow;

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
            var now = _clock.UtcNow;

            CustomsCooProducerProfile? entity = null;
            if (profileId.GetValueOrDefault() > 0)
            {
                entity = await context.CustomsCooProducerProfiles
                    .FirstOrDefaultAsync(item => item.Id == profileId.GetValueOrDefault(), cancellationToken)
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

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            List<CustomsCooProducerProfile> existingProfiles = await LoadExistingProfilesAsync(
                context,
                normalizedInputs,
                cancellationToken).ConfigureAwait(false);
            var byCode = new Dictionary<string, CustomsCooProducerProfile>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, CustomsCooProducerProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (CustomsCooProducerProfile profile in existingProfiles)
            {
                if (!string.IsNullOrWhiteSpace(profile.CiqRegNo))
                {
                    byCode.TryAdd(NormalizeUpperValue(profile.CiqRegNo), profile);
                }
                if (!string.IsNullOrWhiteSpace(profile.PrdcEtpsName))
                {
                    byName.TryAdd(NormalizeText(profile.PrdcEtpsName), profile);
                }
            }

            DateTimeOffset now = _clock.UtcNow;
            foreach (CustomsCooProducerProfileInput input in normalizedInputs)
            {
                CustomsCooProducerProfile? entity = ResolveExisting(input, byCode, byName);
                if (entity == null)
                {
                    entity = new CustomsCooProducerProfile { CreatedAt = now };
                    await context.CustomsCooProducerProfiles.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                }

                ApplyValues(entity, input, now);
                if (!string.IsNullOrWhiteSpace(entity.CiqRegNo))
                {
                    byCode[NormalizeUpperValue(entity.CiqRegNo)] = entity;
                }
                if (!string.IsNullOrWhiteSpace(entity.PrdcEtpsName))
                {
                    byName[NormalizeText(entity.PrdcEtpsName)] = entity;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return normalizedInputs.Count;
        }

        private static async Task<List<CustomsCooProducerProfile>> LoadExistingProfilesAsync(
            AppDbContext context,
            IReadOnlyCollection<CustomsCooProducerProfileInput> inputs,
            CancellationToken cancellationToken)
        {
            string[] codes = inputs
                .Select(item => NormalizeUpperValue(item.CiqRegNo))
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] names = inputs
                .Select(item => NormalizeText(item.PrdcEtpsName).ToUpperInvariant())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return await context.CustomsCooProducerProfiles
                .Where(item =>
                    (item.CiqRegNo != null && codes.Contains(item.CiqRegNo.ToUpper())) ||
                    (item.PrdcEtpsName != null && names.Contains(item.PrdcEtpsName.ToUpper())))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private static CustomsCooProducerProfile? ResolveExisting(
            CustomsCooProducerProfileInput input,
            IReadOnlyDictionary<string, CustomsCooProducerProfile> byCode,
            IReadOnlyDictionary<string, CustomsCooProducerProfile> byName)
        {
            string code = NormalizeUpperValue(input.CiqRegNo);
            if (code.Length > 0 && byCode.TryGetValue(code, out var codeMatch))
            {
                return codeMatch;
            }

            string name = NormalizeText(input.PrdcEtpsName);
            return name.Length > 0 && byName.TryGetValue(name, out var nameMatch)
                ? nameMatch
                : null;
        }

        private static async Task<CustomsCooProducerProfile?> FindExistingAsync(
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

        private static void ApplyValues(CustomsCooProducerProfile target, CustomsCooProducerProfileInput source, DateTimeOffset now)
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

        private static string NormalizeText(string? value)
        {
            return TextSearchHelper.NormalizeValue(value);
        }

        private static string NormalizeUpperValue(string? value)
        {
            return TextSearchHelper.NormalizeUpperValue(value);
        }
    }
}
