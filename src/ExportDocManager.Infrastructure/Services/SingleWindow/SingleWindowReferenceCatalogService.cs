using System.Text;
using System.Text.Json;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowReferenceCatalogService : ISingleWindowReferenceCatalogService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly IAppPathProvider _pathProvider;

        public SingleWindowReferenceCatalogService()
            : this(new RuntimeAppPathProvider())
        {
        }

        public SingleWindowReferenceCatalogService(IAppPathProvider pathProvider)
        {
            _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        public static event Action ReferenceCatalogChanged;

        public async Task<SingleWindowReferenceCatalogModel> LoadEffectiveCatalogAsync(CancellationToken cancellationToken = default)
        {
            return await LoadEffectiveCatalogCoreAsync(
                GetOverrideCatalogPath(_pathProvider),
                GetBundledCatalogPath(_pathProvider),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task SaveOverrideCatalogAsync(SingleWindowReferenceCatalogModel catalog, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            string overridePath = GetOverrideCatalogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
            string json = JsonSerializer.Serialize(NormalizeCatalog(catalog), JsonOptions);
            await AtomicFileHelper.WriteAllTextAtomicAsync(overridePath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            OnReferenceCatalogChanged();
        }

        public async Task ImportCatalogAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("导入文件路径不能为空。", nameof(filePath));
            }

            var catalog = await LoadCatalogFromFileAsync(filePath, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("词典文件内容无效。");
            await SaveOverrideCatalogAsync(catalog, cancellationToken).ConfigureAwait(false);
        }

        public async Task ExportCatalogAsync(SingleWindowReferenceCatalogModel catalog, string filePath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("导出文件路径不能为空。", nameof(filePath));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string json = JsonSerializer.Serialize(NormalizeCatalog(catalog), JsonOptions);
            await AtomicFileHelper.WriteAllTextAtomicAsync(filePath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        public Task ResetToBundledCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileHelper.TryDeleteFile(GetOverrideCatalogPath());
            OnReferenceCatalogChanged();

            return Task.CompletedTask;
        }

        public string GetOverrideCatalogPath()
        {
            return GetOverrideCatalogPath(_pathProvider);
        }

        public static SingleWindowReferenceCatalogModel LoadEffectiveCatalogSnapshot()
        {
            return LoadEffectiveCatalogSnapshot(new RuntimeAppPathProvider());
        }

        public static SingleWindowReferenceCatalogModel LoadEffectiveCatalogSnapshot(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            return LoadEffectiveCatalogCore(GetOverrideCatalogPath(pathProvider), GetBundledCatalogPath(pathProvider));
        }

        private static async Task<SingleWindowReferenceCatalogModel> LoadCatalogFromFileAsync(string filePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                return NormalizeCatalog(JsonSerializer.Deserialize<SingleWindowReferenceCatalogModel>(json, JsonOptions));
            }
            catch
            {
                return null;
            }
        }

        private static SingleWindowReferenceCatalogModel LoadCatalogFromFileSnapshot(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                return NormalizeCatalog(JsonSerializer.Deserialize<SingleWindowReferenceCatalogModel>(json, JsonOptions));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<SingleWindowReferenceCatalogModel> LoadEffectiveCatalogCoreAsync(
            string overrideCatalogPath,
            string bundledCatalogPath,
            CancellationToken cancellationToken)
        {
            var overrideCatalog = await LoadCatalogFromFileAsync(overrideCatalogPath, cancellationToken).ConfigureAwait(false);
            var bundledCatalog = await LoadCatalogFromFileAsync(bundledCatalogPath, cancellationToken)
                .ConfigureAwait(false)
                ?? new SingleWindowReferenceCatalogModel();
            if (overrideCatalog != null)
            {
                return MergeCatalog(overrideCatalog, bundledCatalog);
            }

            return bundledCatalog;
        }

        private static SingleWindowReferenceCatalogModel LoadEffectiveCatalogCore(
            string overrideCatalogPath,
            string bundledCatalogPath)
        {
            var overrideCatalog = LoadCatalogFromFileSnapshot(overrideCatalogPath);
            var bundledCatalog = LoadCatalogFromFileSnapshot(bundledCatalogPath)
                ?? new SingleWindowReferenceCatalogModel();
            if (overrideCatalog != null)
            {
                return MergeCatalog(overrideCatalog, bundledCatalog);
            }

            return bundledCatalog;
        }

        private static SingleWindowReferenceCatalogModel NormalizeCatalog(SingleWindowReferenceCatalogModel catalog)
        {
            catalog ??= new SingleWindowReferenceCatalogModel();
            return new SingleWindowReferenceCatalogModel
            {
                Countries = (catalog.Countries ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferenceCountryEntry
                    {
                        Code = item.Code?.Trim() ?? string.Empty,
                        EnglishName = item.EnglishName?.Trim() ?? string.Empty,
                        ChineseName = item.ChineseName?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList(),
                AcdCountries = (catalog.AcdCountries ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferenceAcdCountryEntry
                    {
                        Code = item.Code?.Trim() ?? string.Empty,
                        ChineseName = item.ChineseName?.Trim() ?? string.Empty,
                        EnglishName = item.EnglishName?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList(),
                Currencies = (catalog.Currencies ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferenceCurrencyEntry
                    {
                        Code = item.Code?.Trim() ?? string.Empty,
                        AcdCode = item.AcdCode?.Trim() ?? string.Empty,
                        AlphaCode = item.AlphaCode?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList(),
                AcdTradeModes = (catalog.AcdTradeModes ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferenceAcdTradeModeEntry
                    {
                        Code = item.Code?.Trim() ?? string.Empty,
                        Name = item.Name?.Trim() ?? string.Empty,
                        Description = item.Description?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList(),
                TransportModes = (catalog.TransportModes ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferenceTransportModeEntry
                    {
                        Value = item.Value?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList(),
                Ports = (catalog.Ports ?? [])
                    .Where(item => item != null)
                    .Select(item => new SingleWindowReferencePortEntry
                    {
                        Value = item.Value?.Trim() ?? string.Empty,
                        Aliases = NormalizeAliases(item.Aliases)
                    })
                    .ToList()
            };
        }

        private static IReadOnlyList<string> NormalizeAliases(IEnumerable<string> aliases)
        {
            return (aliases ?? [])
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static SingleWindowReferenceCatalogModel MergeCatalog(
            SingleWindowReferenceCatalogModel preferred,
            SingleWindowReferenceCatalogModel fallback)
        {
            preferred ??= new SingleWindowReferenceCatalogModel();
            fallback ??= new SingleWindowReferenceCatalogModel();

            return new SingleWindowReferenceCatalogModel
            {
                Countries = preferred.Countries?.Count > 0 ? preferred.Countries : fallback.Countries,
                AcdCountries = preferred.AcdCountries?.Count > 0 ? preferred.AcdCountries : fallback.AcdCountries,
                Currencies = preferred.Currencies?.Count > 0 ? preferred.Currencies : fallback.Currencies,
                AcdTradeModes = preferred.AcdTradeModes?.Count > 0 ? preferred.AcdTradeModes : fallback.AcdTradeModes,
                TransportModes = preferred.TransportModes?.Count > 0 ? preferred.TransportModes : fallback.TransportModes,
                Ports = preferred.Ports?.Count > 0 ? preferred.Ports : fallback.Ports
            };
        }

        private static string GetBundledCatalogPath(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            return Path.Combine(pathProvider.ResourceRoot, "SingleWindow", "singlewindow_reference_catalogs.json");
        }

        private static string GetOverrideCatalogPath(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            return Path.Combine(pathProvider.SingleWindowRoot, "singlewindow_reference_catalogs.override.json");
        }

        private static void OnReferenceCatalogChanged()
        {
            ReferenceCatalogChanged?.Invoke();
        }
    }
}
