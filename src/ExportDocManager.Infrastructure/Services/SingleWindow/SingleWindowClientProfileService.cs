using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowClientProfileService : ISingleWindowClientProfileService
    {
        private static readonly SemaphoreSlim ProfileMutationLock = new(1, 1);
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ISingleWindowStationIdentityService _stationIdentity;
        private readonly IAppPathProvider _pathProvider;
        private readonly LocalSecretProtector _secretProtector;
        private readonly bool _isSqlite;

        public SingleWindowClientProfileService(
            IDbContextFactory<AppDbContext> contextFactory,
            ISingleWindowStationIdentityService stationIdentity,
            IAppPathProvider pathProvider,
            DatabaseConnectionSettings databaseSettings)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _stationIdentity = stationIdentity ?? throw new ArgumentNullException(nameof(stationIdentity));
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
            _secretProtector = new LocalSecretProtector(_pathProvider);
            _isSqlite = !DatabaseModeHelper.UsesPostgreSql(
                databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings)));
        }

        public async Task<IReadOnlyList<SwClientProfile>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureSqliteStation();
            string stationKey = await GetStationKeyAsync(cancellationToken).ConfigureAwait(false);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var profiles = await context.SwClientProfiles
                .AsNoTracking()
                .Where(item => item.StationKey == stationKey && item.IsEnabled)
                .OrderByDescending(item => item.IsActive)
                .ThenBy(item => item.ProfileName)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var profile in profiles)
            {
                profile.StationAssignmentCode = SingleWindowStationAssignmentCode.Encode(
                    profile,
                    _secretProtector);
            }

            return profiles;
        }

        public async Task<SwClientProfile> GetActiveAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureSqliteStation();
            string stationKey = await GetStationKeyAsync(cancellationToken).ConfigureAwait(false);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var profile = await context.SwClientProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.StationKey == stationKey && item.IsEnabled && item.IsActive,
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile == null)
            {
                throw new ResourceNotFoundException(
                    "本持卡机尚未启用操作档案。请先创建公司抬头与操作卡档案，并设为当前档案。");
            }
            profile.StationAssignmentCode = SingleWindowStationAssignmentCode.Encode(
                profile,
                _secretProtector);
            return profile;
        }

        public async Task<int> SaveAsync(
            SingleWindowClientProfileUpdate update,
            CancellationToken cancellationToken = default)
        {
            await ProfileMutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SaveCoreAsync(update, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ProfileMutationLock.Release();
            }
        }

        private async Task<int> SaveCoreAsync(
            SingleWindowClientProfileUpdate update,
            CancellationToken cancellationToken)
        {
            EnsureSqliteStation();
            ArgumentNullException.ThrowIfNull(update);

            string profileName = NormalizeRequired(update.ProfileName, 80, "档案名称");
            string companyScope = NormalizeRequired(update.CompanyScope, 120, "公司抬头");
            string cardIdentifier = NormalizeRequired(update.CardIdentifier, 120, "操作卡标识");
            if (!update.CanSubmitCustomsCoo && !update.CanSubmitAgentConsignment)
            {
                throw new ServiceValidationException("操作档案必须至少启用一种单一窗口业务能力。");
            }

            string requestedProfileKey = NormalizeProfileKey(update.ProfileKey, allowEmpty: true);
            string profileKey = string.IsNullOrWhiteSpace(requestedProfileKey)
                ? $"SWP-{Guid.NewGuid():N}".ToUpperInvariant()
                : requestedProfileKey;
            string customsCooRoot = NormalizeOptionalPath(update.CustomsCooClientRootPath);
            string agentConsignmentRoot = NormalizeOptionalPath(update.AgentConsignmentClientRootPath);
            if (update.CanSubmitCustomsCoo && string.IsNullOrWhiteSpace(customsCooRoot))
            {
                customsCooRoot = GetBuiltInClientRoot(profileKey, SingleWindowBusinessType.CustomsCoo);
            }

            if (update.CanSubmitAgentConsignment && string.IsNullOrWhiteSpace(agentConsignmentRoot))
            {
                agentConsignmentRoot = GetBuiltInClientRoot(profileKey, SingleWindowBusinessType.AgentConsignment);
            }

            EnsureBusinessRootsAreIndependent(customsCooRoot, agentConsignmentRoot);

            string stationKey = await GetStationKeyAsync(cancellationToken).ConfigureAwait(false);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var stationProfiles = await context.SwClientProfiles
                .Where(item => item.StationKey == stationKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var profile = stationProfiles.FirstOrDefault(item =>
                string.Equals(item.ProfileKey, profileKey, StringComparison.Ordinal));
            if (profile == null && !string.IsNullOrWhiteSpace(requestedProfileKey))
            {
                throw new ResourceNotFoundException("未找到要修改的本机操作档案。");
            }

            if (profile != null &&
                (!string.Equals(profile.CompanyScope, companyScope, StringComparison.Ordinal) ||
                 !string.Equals(profile.CardIdentifier, cardIdentifier, StringComparison.Ordinal)))
            {
                throw new ResourceConflictException(
                    "操作档案创建后不能原地更换公司抬头或操作卡标识；如需换卡或切换抬头，请新增档案。档案名称、业务能力和交接目录仍可修改。");
            }

            var otherProfiles = stationProfiles
                .Where(item => profile == null || item.Id != profile.Id)
                .ToList();
            if (otherProfiles.Any(item =>
                    string.Equals(item.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ResourceConflictException("本持卡机已存在同名操作档案，请使用便于区分的档案名称。");
            }

            if (otherProfiles.Any(item =>
                    string.Equals(item.CompanyScope, companyScope, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.CardIdentifier, cardIdentifier, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ResourceConflictException("该公司抬头与操作卡已经建立档案，无需重复创建。");
            }

            EnsureProfileRootsAreIndependent(
                customsCooRoot,
                agentConsignmentRoot,
                otherProfiles.Where(item => item.IsEnabled));

            profile ??= new SwClientProfile
            {
                ProfileKey = profileKey,
                StationKey = stationKey,
                ProtectedHandoffSecret = SingleWindowStationAssignmentCode.CreateProtectedSecret(_secretProtector)
            };

            if (string.IsNullOrWhiteSpace(profile.ProtectedHandoffSecret))
            {
                profile.ProtectedHandoffSecret = SingleWindowStationAssignmentCode.CreateProtectedSecret(_secretProtector);
            }
            else
            {
                _ = SingleWindowStationAssignmentCode.UnprotectProfileSecret(profile, _secretProtector);
            }

            foreach (var item in stationProfiles)
            {
                item.IsActive = false;
            }

            profile.ProfileName = profileName;
            profile.MachineName = Environment.MachineName;
            profile.CompanyScope = companyScope;
            profile.CardIdentifier = cardIdentifier;
            profile.CustomsCooClientRootPath = customsCooRoot;
            profile.AgentConsignmentClientRootPath = agentConsignmentRoot;
            profile.CanSubmitCustomsCoo = update.CanSubmitCustomsCoo;
            profile.CanSubmitAgentConsignment = update.CanSubmitAgentConsignment;
            profile.IsEnabled = true;
            profile.IsActive = true;
            profile.UpdatedAt = DateTime.UtcNow;

            EnsureClientFolderStructure(customsCooRoot);
            EnsureClientFolderStructure(agentConsignmentRoot);

            if (profile.Id <= 0)
            {
                await context.SwClientProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return profile.Id;
        }

        public async Task ActivateAsync(
            string profileKey,
            CancellationToken cancellationToken = default)
        {
            await ProfileMutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ActivateCoreAsync(profileKey, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ProfileMutationLock.Release();
            }
        }

        private async Task ActivateCoreAsync(
            string profileKey,
            CancellationToken cancellationToken)
        {
            EnsureSqliteStation();
            string normalizedProfileKey = NormalizeProfileKey(profileKey, allowEmpty: false);
            string stationKey = await GetStationKeyAsync(cancellationToken).ConfigureAwait(false);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var stationProfiles = await context.SwClientProfiles
                .Where(item => item.StationKey == stationKey && item.IsEnabled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var selected = stationProfiles.FirstOrDefault(item =>
                string.Equals(item.ProfileKey, normalizedProfileKey, StringComparison.Ordinal))
                ?? throw new ResourceNotFoundException("未找到要启用的本机操作档案。");

            foreach (var profile in stationProfiles)
            {
                profile.IsActive = profile.Id == selected.Id;
            }

            selected.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> GetStationKeyAsync(CancellationToken cancellationToken)
        {
            return await _stationIdentity
                .GetCurrentStationKeyAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private void EnsureSqliteStation()
        {
            if (!_isSqlite)
            {
                throw new ServiceValidationException(
                    "持卡操作机仅支持独立 SQLite 单机版；PostgreSQL 网络版只负责制单和回执归档。");
            }
        }

        private string GetBuiltInClientRoot(
            string profileKey,
            SingleWindowBusinessType businessType)
        {
            return SingleWindowClientProfilePathResolver.GetBuiltInBusinessRoot(
                _pathProvider.SingleWindowRoot,
                profileKey,
                businessType);
        }

        private static string NormalizeOptionalPath(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : SingleWindowClientProfilePathResolver.NormalizeClientRootPath(value);
        }

        private static void EnsureBusinessRootsAreIndependent(
            string customsCooRoot,
            string agentConsignmentRoot)
        {
            if (PathsOverlap(customsCooRoot, agentConsignmentRoot))
            {
                throw new ServiceValidationException(
                    "海关原产地证与报关代理委托的数据目录不能相同或互相包含。");
            }
        }

        private static void EnsureProfileRootsAreIndependent(
            string customsCooRoot,
            string agentConsignmentRoot,
            IEnumerable<SwClientProfile> otherProfiles)
        {
            string[] requestedRoots = [customsCooRoot, agentConsignmentRoot];
            foreach (var other in otherProfiles)
            {
                string[] existingRoots =
                [
                    other.CustomsCooClientRootPath ?? string.Empty,
                    other.AgentConsignmentClientRootPath ?? string.Empty
                ];
                if (requestedRoots.Any(requested =>
                        existingRoots.Any(existing => PathsOverlap(requested, existing))))
                {
                    throw new ResourceConflictException(
                        $"当前目录与操作档案“{other.ProfileName}”的目录相同或互相包含；不同公司和操作卡必须使用独立目录。");
                }
            }
        }

        private static bool PathsOverlap(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) &&
                   !string.IsNullOrWhiteSpace(second) &&
                   (PathBoundaryHelper.IsWithinRoot(first, second) ||
                    PathBoundaryHelper.IsWithinRoot(second, first));
        }

        private static void EnsureClientFolderStructure(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "OutBox"));
            Directory.CreateDirectory(Path.Combine(rootPath, "SentBox"));
            Directory.CreateDirectory(Path.Combine(rootPath, "InBox"));
            Directory.CreateDirectory(Path.Combine(rootPath, "FailBox"));
        }

        private static string NormalizeRequired(string value, int maxLength, string fieldName)
        {
            string normalized = NormalizeOptional(value, maxLength);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ServiceValidationException($"{fieldName}不能为空。");
            }

            return normalized;
        }

        private static string NormalizeOptional(string value, int maxLength)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length > maxLength)
            {
                throw new ServiceValidationException($"配置内容不能超过 {maxLength} 个字符。");
            }

            return normalized;
        }

        private static string NormalizeProfileKey(string value, bool allowEmpty)
        {
            string normalized = NormalizeOptional(value, 64);
            if (allowEmpty && string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (normalized.Length != 36 ||
                !normalized.StartsWith("SWP-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(normalized[4..], "N", out _))
            {
                throw new ServiceValidationException("操作档案标识无效。");
            }

            return normalized;
        }
    }
}
