using System.Collections.Concurrent;
using System.Text.Json;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class LocalPreferenceService : ILocalPreferenceService
    {
        private const string PreferencesFileName = "local-preferences.json";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        private readonly string _filePath;
        private readonly object _stateLock = new();
        private Dictionary<string, string> _preferenceStates;

        public LocalPreferenceService()
            : this(new RuntimeAppPathProvider())
        {
        }

        public LocalPreferenceService(IAppPathProvider pathProvider)
            : this(pathProvider, null)
        {
        }

        public LocalPreferenceService(string filePath)
            : this(new RuntimeAppPathProvider(), filePath)
        {
        }

        public LocalPreferenceService(IAppPathProvider pathProvider, string filePath)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            _filePath = ResolvePreferencePath(pathProvider, filePath);
            _preferenceStates = LoadPreferenceStates(_filePath);
        }

        public T Load<T>(string key) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return new T();
            }

            string preferenceJson;
            lock (_stateLock)
            {
                _preferenceStates.TryGetValue(key.Trim(), out preferenceJson);
            }

            if (string.IsNullOrWhiteSpace(preferenceJson))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(preferenceJson, JsonOptions) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public async Task SaveAsync<T>(string key, T state, CancellationToken cancellationToken = default) where T : class
        {
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            string normalizedKey = key.Trim();
            string serializedState = JsonSerializer.Serialize(state, JsonOptions);

            var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
            await saveLock.WaitAsync(cancellationToken);
            try
            {
                var snapshot = LoadPreferenceStates(_filePath);
                if (snapshot.TryGetValue(normalizedKey, out var existingState) &&
                    string.Equals(existingState, serializedState, StringComparison.Ordinal))
                {
                    ReplaceInMemoryState(snapshot);
                    return;
                }

                snapshot[normalizedKey] = serializedState;
                ReplaceInMemoryState(snapshot);

                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                await AtomicFileHelper.WriteAllTextAtomicAsync(_filePath, json).ConfigureAwait(false);
            }
            finally
            {
                saveLock.Release();
            }
        }

        private void ReplaceInMemoryState(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            var replacement = NormalizePreferenceStates(snapshot);
            lock (_stateLock)
            {
                _preferenceStates = replacement;
            }
        }

        private static Dictionary<string, string> LoadPreferenceStates(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return CreateEmptyState();
                }

                string json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return CreateEmptyState();
                }

                var states = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                return NormalizePreferenceStates(states);
            }
            catch
            {
                return CreateEmptyState();
            }
        }

        private static Dictionary<string, string> NormalizePreferenceStates(Dictionary<string, string> states)
        {
            var normalizedStates = CreateEmptyState();
            if (states == null)
            {
                return normalizedStates;
            }

            foreach (var (key, value) in states)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                normalizedStates[key.Trim()] = value;
            }

            return normalizedStates;
        }

        private static string ResolvePreferencePath(IAppPathProvider pathProvider, string filePath)
        {
            var preferencePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(pathProvider.AppRoot, PreferencesFileName)
                : filePath.Trim();

            return Path.GetFullPath(Path.IsPathRooted(preferencePath)
                ? preferencePath
                : Path.Combine(pathProvider.AppRoot, preferencePath));
        }

        private static Dictionary<string, string> CreateEmptyState()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
