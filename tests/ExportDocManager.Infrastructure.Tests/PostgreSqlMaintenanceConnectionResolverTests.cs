#nullable enable

using ExportDocManager.DataAccess;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Infrastructure.Tests;

[Collection(LocalSecretProtectionCollection.Name)]
public sealed class PostgreSqlMaintenanceConnectionResolverTests
{
    [Fact]
    public void Resolve_ShouldUseApplicationCredentialsWhenSeparationIsNotConfigured()
    {
        WithCleanEnvironment(() =>
        {
            var settings = CreateApplicationSettings();
            var profile = PostgreSqlMaintenanceConnectionResolver.Resolve(
                settings,
                new TestAppPathProvider(Path.GetTempPath()));

            Assert.False(profile.UsesDedicatedCredentials);
            Assert.Equal("exportdoc_app", profile.OwnerRole);
            Assert.Equal("exportdoc_app", profile.ConnectionSettings.PostgreSqlUsername);
            Assert.Equal("application-secret", profile.ConnectionSettings.PostgreSqlPassword);
        });
    }

    [Fact]
    public void Resolve_ShouldUseDedicatedMaintenanceCredentialsAndOwnerRole()
    {
        WithCleanEnvironment(() =>
        {
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.UsernameEnvironmentVariable,
                " exportdoc_maintenance ");
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.PasswordEnvironmentVariable,
                "maintenance-secret");
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.OwnerRoleEnvironmentVariable,
                " exportdoc_owner ");

            var profile = PostgreSqlMaintenanceConnectionResolver.Resolve(
                CreateApplicationSettings(),
                new TestAppPathProvider(Path.GetTempPath()));

            Assert.True(profile.UsesDedicatedCredentials);
            Assert.Equal("exportdoc_owner", profile.OwnerRole);
            Assert.Equal("exportdoc_maintenance", profile.ConnectionSettings.PostgreSqlUsername);
            Assert.Equal("maintenance-secret", profile.ConnectionSettings.PostgreSqlPassword);
            Assert.Equal("exportdoc", profile.ConnectionSettings.PostgreSqlDatabase);
        });
    }

    [Fact]
    public void Resolve_ShouldRejectPartialSeparationConfiguration()
    {
        WithCleanEnvironment(() =>
        {
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.UsernameEnvironmentVariable,
                "exportdoc_maintenance");

            var exception = Assert.Throws<ServiceValidationException>(() =>
                PostgreSqlMaintenanceConnectionResolver.Resolve(
                    CreateApplicationSettings(),
                    new TestAppPathProvider(Path.GetTempPath())));

            Assert.Contains("权限分离配置不完整", exception.Message, StringComparison.Ordinal);
        });
    }

    private static DatabaseConnectionSettings CreateApplicationSettings() => new()
    {
        Provider = DatabaseConnectionSettings.PostgreSqlProvider,
        PostgreSqlHost = "postgres",
        PostgreSqlPort = 5432,
        PostgreSqlDatabase = "exportdoc",
        PostgreSqlUsername = "exportdoc_app",
        PostgreSqlPassword = "application-secret",
        PostgreSqlAdditionalOptions = "Pooling=true"
    };

    private static void WithCleanEnvironment(Action action)
    {
        string? previousUsername = Environment.GetEnvironmentVariable(
            PostgreSqlMaintenanceConnectionResolver.UsernameEnvironmentVariable);
        string? previousPassword = Environment.GetEnvironmentVariable(
            PostgreSqlMaintenanceConnectionResolver.PasswordEnvironmentVariable);
        string? previousPasswordFile = Environment.GetEnvironmentVariable(
            PostgreSqlMaintenanceConnectionResolver.PasswordFileEnvironmentVariable);
        string? previousOwner = Environment.GetEnvironmentVariable(
            PostgreSqlMaintenanceConnectionResolver.OwnerRoleEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.UsernameEnvironmentVariable,
                null);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.PasswordEnvironmentVariable,
                null);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.PasswordFileEnvironmentVariable,
                null);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.OwnerRoleEnvironmentVariable,
                null);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.UsernameEnvironmentVariable,
                previousUsername);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.PasswordEnvironmentVariable,
                previousPassword);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.PasswordFileEnvironmentVariable,
                previousPasswordFile);
            Environment.SetEnvironmentVariable(
                PostgreSqlMaintenanceConnectionResolver.OwnerRoleEnvironmentVariable,
                previousOwner);
        }
    }

    private sealed class TestAppPathProvider : IAppPathProvider
    {
        public TestAppPathProvider(string root)
        {
            AppRoot = root;
            DataRoot = root;
        }

        public string AppRoot { get; }
        public string DataRoot { get; }
        public string DatabaseRoot => Path.Combine(DataRoot, "Database");
        public string TemplateRoot => Path.Combine(AppRoot, "Templates");
        public string UserTemplateRoot => Path.Combine(DataRoot, "Templates");
        public string ResourceRoot => Path.Combine(AppRoot, "Resources");
        public string BrowserRoot => Path.Combine(AppRoot, "Browsers");
        public string ToolRoot => Path.Combine(AppRoot, "Tools");
        public string FileRoot => Path.Combine(DataRoot, "Files");
        public string ExportRoot => Path.Combine(DataRoot, "Exports");
        public string BackupRoot => Path.Combine(DataRoot, "Backups");
        public string SingleWindowRoot => Path.Combine(DataRoot, "SingleWindow");
        public string OcrModelRoot => Path.Combine(AppRoot, "OcrModels");
        public string LogRoot => Path.Combine(DataRoot, "Logs");
        public string CacheRoot => Path.Combine(DataRoot, "Cache");
        public string ConfigRoot => Path.Combine(DataRoot, "Config");
        public string SecurityRoot => Path.Combine(DataRoot, "Security");
        public string WebViewRoot => Path.Combine(DataRoot, "WebView");
    }
}
