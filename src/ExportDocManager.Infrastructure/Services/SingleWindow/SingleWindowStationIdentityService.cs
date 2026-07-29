using System.Text;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowStationIdentityService : ISingleWindowStationIdentityService
    {
        private static readonly SemaphoreSlim IdentityLock = new(1, 1);
        private readonly IAppPathProvider _pathProvider;

        public SingleWindowStationIdentityService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public async Task<string> GetCurrentStationKeyAsync(
            CancellationToken cancellationToken = default)
        {
            string identityPath = Path.Combine(
                _pathProvider.SecurityRoot,
                "SingleWindow",
                "station.id");

            await IdentityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(identityPath))
                {
                    string stored = (await File.ReadAllTextAsync(identityPath, cancellationToken)
                            .ConfigureAwait(false))
                        .Trim();
                    if (IsValidStationKey(stored))
                    {
                        return stored;
                    }

                    throw new InvalidDataException(
                        "本持卡机身份文件 station.id 已损坏。请从备份恢复该文件；确认无需保留原档案和批次绑定后，才可由管理员删除并重新初始化。");
                }

                string key = $"SWS-{Guid.NewGuid():N}".ToUpperInvariant();
                Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
                await AtomicFileHelper.WriteAllTextAtomicAsync(
                        identityPath,
                        key,
                        Encoding.UTF8,
                        cancellationToken)
                    .ConfigureAwait(false);
                return key;
            }
            finally
            {
                IdentityLock.Release();
            }
        }

        private static bool IsValidStationKey(string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.Length == 36 &&
                   normalized.StartsWith("SWS-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(normalized[4..], "N", out _);
        }
    }
}
