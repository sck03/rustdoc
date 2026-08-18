using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Infrastructure.Tests;

[Collection(LocalSecretProtectionCollection.Name)]
public sealed class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_ShouldRejectPlaintextPostgreSqlPasswordWithoutCreatingKeyFile()
    {
        await WithLocalKeyFileModeAsync(async () =>
        {
            string root = CreateTempRoot();
            try
            {
                var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                string settingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
                var source = CreateSettings("email-plain", "webdav-plain", "postgres-plain", "ai-plain");
                await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(source));

                var service = new SettingsService(paths);
                var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync());

                Assert.Contains("不能以明文", error.Message, StringComparison.Ordinal);
                Assert.Contains(DbHelper.PostgreSqlPasswordEnvironmentVariable, error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(paths.SecurityRoot, LocalSecretProtector.MasterKeyFileName)));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task SaveAndLoadAsync_ShouldEncryptOnceAndRestorePlaintextInMemory()
    {
        await WithLocalKeyFileModeAsync(async () =>
        {
            string root = CreateTempRoot();
            try
            {
                var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                var service = new SettingsService(paths);
                await UpdateSecretsAsync(service, "email-secret", "webdav-secret", "postgres-secret", "ai-secret");

                Assert.Equal("email-secret", service.Settings.Email.Password);
                await service.UpdateAsync(_ => true);
                Assert.Equal("email-secret", service.Settings.Email.Password);

                string storedJson = await File.ReadAllTextAsync(Path.Combine(paths.ConfigRoot, "appsettings.json"));
                Assert.Contains("edm-aesgcm-v1:", storedJson, StringComparison.Ordinal);
                Assert.DoesNotContain("email-secret", storedJson, StringComparison.Ordinal);
                Assert.DoesNotContain("webdav-secret", storedJson, StringComparison.Ordinal);
                Assert.DoesNotContain("postgres-secret", storedJson, StringComparison.Ordinal);
                Assert.DoesNotContain("ai-secret", storedJson, StringComparison.Ordinal);

                var loaded = new SettingsService(paths);
                await loaded.LoadAsync();
                Assert.Equal("email-secret", loaded.Settings.Email.Password);
                Assert.Equal("webdav-secret", loaded.Settings.WebDav.Password);
                Assert.Equal("postgres-secret", loaded.Settings.System.PostgreSqlPassword);
                Assert.Equal("ai-secret", loaded.Settings.AI.ApiKey);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadAsync_WhenKeyFileIsMissing_ShouldFailWithoutOverwritingSettings()
    {
        await WithLocalKeyFileModeAsync(async () =>
        {
            string root = CreateTempRoot();
            try
            {
                var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                var writer = new SettingsService(paths);
                await UpdateSecretsAsync(writer, "email-secret", "webdav-secret", "postgres-secret", "ai-secret");

                string settingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
                string originalJson = await File.ReadAllTextAsync(settingsPath);
                File.Delete(Path.Combine(paths.SecurityRoot, LocalSecretProtector.MasterKeyFileName));

                var reader = new SettingsService(paths);
                var error = await Assert.ThrowsAsync<InvalidDataException>(() => reader.LoadAsync());
                Assert.Contains("密钥文件不存在", error.Message, StringComparison.Ordinal);
                Assert.Equal(originalJson, await File.ReadAllTextAsync(settingsPath));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadAsync_WhenCiphertextIsCorrupted_ShouldFailWithoutReplacingFile()
    {
        await WithLocalKeyFileModeAsync(async () =>
        {
            string root = CreateTempRoot();
            try
            {
                var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
                var writer = new SettingsService(paths);
                await UpdateSecretsAsync(writer, "email-secret", string.Empty, string.Empty, string.Empty);

                string settingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
                var stored = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(settingsPath))!;
                string encrypted = stored.Email.Password;
                stored.Email.Password = encrypted[..^1] + (encrypted[^1] == 'A' ? 'B' : 'A');
                string corruptedJson = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(settingsPath, corruptedJson);

                var reader = new SettingsService(paths);
                var error = await Assert.ThrowsAsync<InvalidDataException>(() => reader.LoadAsync());
                Assert.Contains("无法解密", error.Message, StringComparison.Ordinal);
                Assert.Equal(corruptedJson, await File.ReadAllTextAsync(settingsPath));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        });
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsCorrupted_ShouldFailWithoutReplacingFile()
    {
        string root = CreateTempRoot();
        try
        {
            var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            string settingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
            const string corruptedJson = "{ invalid-json";
            await File.WriteAllTextAsync(settingsPath, corruptedJson);

            var service = new SettingsService(paths);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync());

            Assert.Contains("JSON 损坏", error.Message, StringComparison.Ordinal);
            Assert.Equal(corruptedJson, await File.ReadAllTextAsync(settingsPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenSqlitePathEscapesRuntimeDatabaseRoot_ShouldFailWithoutReplacingFile()
    {
        string root = CreateTempRoot();
        try
        {
            var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            string settingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
            var settings = new AppSettings
            {
                System = new SystemSettings
                {
                    DatabaseProvider = "SQLite",
                    SqliteDatabaseFileName = "..\\outside.db"
                }
            };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(settingsPath, json);

            var service = new SettingsService(paths);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync());

            Assert.Contains("SQLite", error.Message, StringComparison.Ordinal);
            Assert.Equal(json, await File.ReadAllTextAsync(settingsPath));
            Assert.Equal(DatabaseConnectionSettings.DefaultSqliteDatabaseFileName,
                service.Settings.System.SqliteDatabaseFileName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_ShouldOmitSealDataFromPaymentTemplates()
    {
        string root = CreateTempRoot();
        try
        {
            var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            var writer = new SettingsService(paths);
            await writer.UpdateAsync(settings =>
            {
                settings.PaymentTemplates =
                [
                    new PaymentTemplateItem
                    {
                        Name = "付款模板",
                        TemplatePath = "user:Internal/payment.html",
                        ReportType = "ExportDocument"
                    }
                ];
                return true;
            });

            var savedItem = Assert.Single(writer.Settings.PaymentTemplates);
            Assert.Equal("PaymentVoucher", savedItem.ReportType);

            string storedJson = await File.ReadAllTextAsync(Path.Combine(paths.ConfigRoot, "appsettings.json"));
            using (var document = JsonDocument.Parse(storedJson))
            {
                var storedItem = document.RootElement.GetProperty("PaymentTemplates")[0];
                Assert.False(storedItem.TryGetProperty("ShowSeal", out _));
            }

            var stored = JsonSerializer.Deserialize<AppSettings>(storedJson)!;
            Assert.Equal("PaymentVoucher", Assert.Single(stored.PaymentTemplates).ReportType);

            var reader = new SettingsService(paths);
            await reader.LoadAsync();
            Assert.Equal("PaymentVoucher", Assert.Single(reader.Settings.PaymentTemplates).ReportType);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void PaymentTemplateSettings_ShouldRejectSealMetadataDuringDeserialization()
    {
        const string json =
            """
            {
              "PaymentTemplates": [
                {
                  "Name": "付款模板",
                  "TemplatePath": "user:Internal/payment.html",
                  "ReportType": "PaymentVoucher",
                  "IsEnabled": true,
                  "ShowSeal": true
                }
              ]
            }
            """;

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>(json));

        Assert.Contains("ShowSeal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsSnapshot_ShouldBeIsolatedFromPersistedState()
    {
        string root = CreateTempRoot();
        try
        {
            var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            var service = new SettingsService(paths);
            await service.UpdateAsync(settings =>
            {
                settings.System.AppName = "Persisted";
                return true;
            });

            var snapshot = service.Settings;
            snapshot.System.AppName = "Mutated outside service";

            Assert.Equal("Persisted", service.Settings.System.AppName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_ShouldSerializeWithoutLosingChanges()
    {
        string root = CreateTempRoot();
        try
        {
            var paths = new RuntimeAppPathProvider(root, Path.Combine(root, "App_Data"));
            var service = new SettingsService(paths);

            await Task.WhenAll(
                service.UpdateAsync(settings =>
                {
                    settings.System.AppName = "Concurrent";
                    return true;
                }),
                service.UpdateAsync(settings =>
                {
                    settings.System.ItemEntryBlankRowCount = 64;
                    return true;
                }));

            Assert.Equal("Concurrent", service.Settings.System.AppName);
            Assert.Equal(64, service.Settings.System.ItemEntryBlankRowCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static AppSettings CreateSettings(
        string emailPassword,
        string webDavPassword,
        string postgreSqlPassword,
        string aiApiKey)
    {
        var settings = new AppSettings();
        ApplySecrets(settings, emailPassword, webDavPassword, postgreSqlPassword, aiApiKey);
        return settings;
    }

    private static void ApplySecrets(
        AppSettings settings,
        string emailPassword,
        string webDavPassword,
        string postgreSqlPassword,
        string aiApiKey)
    {
        settings.Email ??= new EmailConfig();
        settings.WebDav ??= new WebDavSettings();
        settings.System ??= new SystemSettings();
        settings.AI ??= new AISettings();
        settings.Email.Password = emailPassword;
        settings.WebDav.Password = webDavPassword;
        settings.System.PostgreSqlPassword = postgreSqlPassword;
        settings.AI.ApiKey = aiApiKey;
    }

    private static Task<bool> UpdateSecretsAsync(
        SettingsService service,
        string emailPassword,
        string webDavPassword,
        string postgreSqlPassword,
        string aiApiKey) =>
        service.UpdateAsync(settings =>
        {
            ApplySecrets(settings, emailPassword, webDavPassword, postgreSqlPassword, aiApiKey);
            return true;
        });

    private static async Task WithLocalKeyFileModeAsync(Func<Task> action)
    {
        string? previous = Environment.GetEnvironmentVariable(LocalSecretProtector.MasterKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(LocalSecretProtector.MasterKeyEnvironmentVariable, null);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalSecretProtector.MasterKeyEnvironmentVariable, previous);
        }
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            ".codex-runtime",
            "settings-service-tests",
            $"edm-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "App_Data", "Config"));
        Directory.CreateDirectory(Path.Combine(path, "App_Data", "Security"));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "ExportDocManager.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln from test output.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
