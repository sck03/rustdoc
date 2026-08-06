using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExportDocManager.DataAccess;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class ServerMigrationSecurityTests
{
    [Fact]
    public void ParseConfiguredMasterKey_ShouldAcceptHexAndBase64()
    {
        byte[] expected = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        string hex = Convert.ToHexString(expected);
        string base64 = Convert.ToBase64String(expected);

        byte[] fromHex = ServerMigrationService.ParseConfiguredMasterKey(hex);
        byte[] fromBase64 = ServerMigrationService.ParseConfiguredMasterKey(base64);
        try
        {
            Assert.Equal(expected, fromHex);
            Assert.Equal(expected, fromBase64);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fromHex);
            CryptographicOperations.ZeroMemory(fromBase64);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key")]
    [InlineData("0011")]
    public void ParseConfiguredMasterKey_ShouldRejectInvalidValues(string value)
    {
        Assert.Throws<InvalidOperationException>(() => ServerMigrationService.ParseConfiguredMasterKey(value));
    }

    [Theory]
    [InlineData(
        @"C:\source\App_Data\Files\Seals\document.png",
        @"C:\source\App_Data",
        "/srv/exportdoc/App_Data",
        '/',
        "/srv/exportdoc/App_Data/Files/Seals/document.png")]
    [InlineData(
        "/srv/exportdoc/App_Data/Marks/mark.png",
        "/srv/exportdoc/App_Data",
        @"D:\ExportDoc\App_Data",
        '\\',
        @"D:\ExportDoc\App_Data\Marks\mark.png")]
    public void RewriteManagedPath_ShouldTranslateWindowsAndUnixSeparators(
        string value,
        string sourceRoot,
        string targetRoot,
        char targetSeparator,
        string expected)
    {
        Assert.Equal(
            expected,
            ServerMigrationPathRewriter.RewriteManagedPath(
                value,
                sourceRoot,
                targetRoot,
                targetSeparator));
    }

    [Fact]
    public void RewriteManagedPath_ShouldNotRewriteSiblingPrefix()
    {
        const string value = "/srv/exportdoc/App_Data-Other/Marks/mark.png";
        Assert.Equal(
            value,
            ServerMigrationPathRewriter.RewriteManagedPath(
                value,
                "/srv/exportdoc/App_Data",
                @"D:\ExportDoc\App_Data",
                '\\'));
    }

    [Fact]
    public void RewriteManagedPath_ShouldRespectSourcePlatformCaseSemantics()
    {
        Assert.Equal(
            @"D:\ExportDoc\App_Data\Marks\mark.png",
            ServerMigrationPathRewriter.RewriteManagedPath(
                @"c:\SOURCE\App_Data\Marks\mark.png",
                @"C:\source\App_Data",
                @"D:\ExportDoc\App_Data",
                '\\'));

        const string unixValue = "/srv/exportdoc/app_data/Marks/mark.png";
        Assert.Equal(
            unixValue,
            ServerMigrationPathRewriter.RewriteManagedPath(
                unixValue,
                "/srv/exportdoc/App_Data",
                @"D:\ExportDoc\App_Data",
                '\\'));
    }

    [Fact]
    public void ManagedDataPathResolver_ShouldPersistPortableRelativePathsAndRejectEscape()
    {
        string root = CreateTestRoot("managed-paths");
        try
        {
            string appRoot = Path.Combine(root, "app");
            string dataRoot = Path.Combine(root, "data");
            var paths = new RuntimeAppPathProvider(appRoot, dataRoot);
            string marksRoot = Path.Combine(paths.DataRoot, "Marks");
            Directory.CreateDirectory(marksRoot);
            string fullPath = Path.Combine(marksRoot, "mark.png");
            File.WriteAllBytes(fullPath, [1, 2, 3]);

            string stored = ManagedDataPathResolver.ToStoredPath(
                paths,
                fullPath,
                marksRoot,
                "Marks");

            Assert.Equal("Marks/mark.png", stored);
            Assert.Equal(
                Path.GetFullPath(fullPath),
                ManagedDataPathResolver.ResolveStoredPath(
                    paths,
                    stored,
                    marksRoot,
                    "Marks"));
            Assert.Throws<InvalidDataException>(() =>
                ManagedDataPathResolver.NormalizeStoredPath(fullPath, "Marks"));
            Assert.Throws<InvalidDataException>(() =>
                ManagedDataPathResolver.NormalizeStoredPath("Marks/../outside.png", "Marks"));
            Assert.Throws<InvalidDataException>(() =>
                ManagedDataPathResolver.NormalizeStoredPath("Files/outside.png", "Marks"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void WriteMergedAppSettings_ShouldKeepTargetConnectionAndRemovePlaintextPassword()
    {
        string root = CreateTestRoot("merged-settings");
        try
        {
            string source = Path.Combine(root, "source.json");
            string target = Path.Combine(root, "target.json");
            File.WriteAllText(
                source,
                """
                {
                  "System": {
                    "DatabaseProvider": "PostgreSql",
                    "PostgreSqlHost": "source-db",
                    "PostgreSqlPort": 5432,
                    "PostgreSqlDatabase": "source",
                    "PostgreSqlUsername": "source-user",
                    "PostgreSqlPassword": "source-plaintext"
                  },
                  "Probe": "preserved"
                }
                """);
            var targetSettings = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "target-db",
                PostgreSqlPort = 5544,
                PostgreSqlDatabase = "target",
                PostgreSqlUsername = "target-user",
                PostgreSqlPassword = "runtime-secret",
                PostgreSqlAdditionalOptions = "SSL Mode=Require"
            };

            ServerMigrationFileTransaction.WriteMergedAppSettings(
                source,
                target,
                targetSettings);

            string json = File.ReadAllText(target);
            JsonObject rootNode = JsonNode.Parse(json)!.AsObject();
            JsonObject system = rootNode["System"]!.AsObject();
            Assert.Equal("target-db", system["PostgreSqlHost"]!.GetValue<string>());
            Assert.Equal(5544, system["PostgreSqlPort"]!.GetValue<int>());
            Assert.Equal("target", system["PostgreSqlDatabase"]!.GetValue<string>());
            Assert.Equal("target-user", system["PostgreSqlUsername"]!.GetValue<string>());
            Assert.Equal(string.Empty, system["PostgreSqlPassword"]!.GetValue<string>());
            Assert.Equal("preserved", rootNode["Probe"]!.GetValue<string>());
            Assert.DoesNotContain("source-plaintext", json, StringComparison.Ordinal);
            Assert.DoesNotContain("runtime-secret", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void FileTransaction_ShouldReplaceAndRollbackFilesTemplatesSingleWindowAndMarks()
    {
        string root = CreateTestRoot("file-transaction");
        try
        {
            var paths = new RuntimeAppPathProvider(
                Path.Combine(root, "app"),
                Path.Combine(root, "data"));
            string marksRoot = Path.Combine(paths.DataRoot, "Marks");
            WriteText(Path.Combine(paths.FileRoot, "old.txt"), "old-files");
            WriteText(Path.Combine(paths.UserTemplateRoot, "old.txt"), "old-templates");
            WriteText(Path.Combine(paths.SingleWindowRoot, "old.txt"), "old-single-window");
            WriteText(Path.Combine(marksRoot, "old.txt"), "old-marks");
            string targetSettingsPath = Path.Combine(paths.ConfigRoot, "appsettings.json");
            WriteText(targetSettingsPath, "{\"Probe\":\"old-config\"}");
            byte[] oldKey = Enumerable.Repeat((byte)1, 32).ToArray();
            string targetKeyPath = Path.Combine(
                paths.SecurityRoot,
                LocalSecretProtector.MasterKeyFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(targetKeyPath)!);
            File.WriteAllBytes(targetKeyPath, oldKey);

            string stagingRoot = Path.Combine(root, "staging");
            var stagedFiles = new Dictionary<string, string>
            {
                ["Data/Files/new.txt"] = "new-files",
                ["Data/Templates/new.txt"] = "new-templates",
                ["Data/SingleWindow/new.txt"] = "new-single-window",
                ["Data/Marks/new.txt"] = "new-marks",
                ["Config/appsettings.json"] = "{\"System\":{\"PostgreSqlPassword\":\"plaintext\"},\"Probe\":\"new-config\"}",
                ["Database/postgresql.dump"] = "dump"
            };
            foreach ((string relative, string content) in stagedFiles)
            {
                WriteText(ServerMigrationService.ResolvePath(stagingRoot, relative), content);
            }
            byte[] newKey = Enumerable.Repeat((byte)2, 32).ToArray();
            string stagedKey = ServerMigrationService.ResolvePath(
                stagingRoot,
                ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedKey)!);
            File.WriteAllBytes(stagedKey, newKey);

            var marker = new PendingServerMigrationRestore
            {
                SchemaVersion = ServerMigrationLayout.SchemaVersion,
                PackageId = Guid.NewGuid().ToString("N"),
                StagingDirectoryName = "staging",
                Manifest = new ServerMigrationManifest
                {
                    SchemaVersion = ServerMigrationLayout.SchemaVersion,
                    PackageId = Guid.NewGuid().ToString("N"),
                    Files = stagedFiles.Keys
                        .Append(ServerMigrationLayout.SecurityEntry(LocalSecretProtector.MasterKeyFileName))
                        .Select(relative => new ServerMigrationFileManifest { RelativePath = relative })
                        .ToList()
                }
            };
            string safetyRoot = Path.Combine(root, "safety");
            var databaseSettings = new DatabaseConnectionSettings
            {
                Provider = DatabaseConnectionSettings.PostgreSqlProvider,
                PostgreSqlHost = "target-db",
                PostgreSqlPort = 5432,
                PostgreSqlDatabase = "target",
                PostgreSqlUsername = "target-user",
                PostgreSqlPassword = "runtime-secret"
            };

            ServerMigrationFileTransactionState state = ServerMigrationFileTransaction.Prepare(
                paths,
                stagingRoot,
                safetyRoot,
                marker,
                databaseSettings);
            ServerMigrationFileTransaction.Apply(state);

            Assert.False(File.Exists(Path.Combine(paths.FileRoot, "old.txt")));
            Assert.Equal("new-files", File.ReadAllText(Path.Combine(paths.FileRoot, "new.txt")));
            Assert.Equal("new-templates", File.ReadAllText(Path.Combine(paths.UserTemplateRoot, "new.txt")));
            Assert.Equal("new-single-window", File.ReadAllText(Path.Combine(paths.SingleWindowRoot, "new.txt")));
            Assert.Equal("new-marks", File.ReadAllText(Path.Combine(marksRoot, "new.txt")));
            Assert.Equal(newKey, File.ReadAllBytes(targetKeyPath));
            Assert.DoesNotContain("plaintext", File.ReadAllText(targetSettingsPath), StringComparison.Ordinal);
            Assert.DoesNotContain("runtime-secret", File.ReadAllText(targetSettingsPath), StringComparison.Ordinal);

            ServerMigrationFileTransaction.Rollback(safetyRoot);

            Assert.Equal("old-files", File.ReadAllText(Path.Combine(paths.FileRoot, "old.txt")));
            Assert.Equal("old-templates", File.ReadAllText(Path.Combine(paths.UserTemplateRoot, "old.txt")));
            Assert.Equal("old-single-window", File.ReadAllText(Path.Combine(paths.SingleWindowRoot, "old.txt")));
            Assert.Equal("old-marks", File.ReadAllText(Path.Combine(marksRoot, "old.txt")));
            Assert.Equal("{\"Probe\":\"old-config\"}", File.ReadAllText(targetSettingsPath));
            Assert.Equal(oldKey, File.ReadAllBytes(targetKeyPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyPendingRestoreAsync_ShouldDiscardCompletedMarkerWithoutReapplyingRestore()
    {
        string root = CreateTestRoot("completed-marker");
        try
        {
            var paths = new RuntimeAppPathProvider(
                Path.Combine(root, "app"),
                Path.Combine(root, "data"));
            string packageId = Guid.NewGuid().ToString("N");
            string stagingDirectoryName = $"pending-{packageId}";
            string stagingRoot = Path.Combine(
                ServerMigrationManager.GetControlRoot(paths),
                stagingDirectoryName);
            Directory.CreateDirectory(stagingRoot);
            WriteText(Path.Combine(stagingRoot, "probe.txt"), "completed");
            var marker = CreateMarker(packageId, stagingDirectoryName, ServerMigrationRestorePhase.Completed);
            string markerPath = ServerMigrationManager.GetPendingMarkerPath(paths);
            WriteText(
                markerPath,
                JsonSerializer.Serialize(marker, ServerMigrationService.JsonOptions));

            await ServerMigrationManager.ApplyPendingRestoreAsync(paths);

            Assert.False(File.Exists(markerPath));
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ApplyPendingRestoreAsync_ShouldQuarantineMarkerWithUnexpectedStagingDirectory()
    {
        string root = CreateTestRoot("invalid-marker-path");
        try
        {
            var paths = new RuntimeAppPathProvider(
                Path.Combine(root, "app"),
                Path.Combine(root, "data"));
            string packageId = Guid.NewGuid().ToString("N");
            var marker = CreateMarker(
                packageId,
                $"pending-{Guid.NewGuid():N}",
                ServerMigrationRestorePhase.Pending);
            string markerPath = ServerMigrationManager.GetPendingMarkerPath(paths);
            WriteText(
                markerPath,
                JsonSerializer.Serialize(marker, ServerMigrationService.JsonOptions));

            await ServerMigrationManager.ApplyPendingRestoreAsync(paths);

            Assert.False(File.Exists(markerPath));
            ServerMigrationRestoreStatusSnapshot status = ServerMigrationManager.ReadStatus(paths);
            Assert.NotNull(status);
            Assert.Equal(ServerMigrationRestorePhase.Failed, status.Phase);
            Assert.Contains("恢复标记无效", status.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void MigrationSource_ShouldRejectLinksBeforeRecursionAndExcludeCertificates()
    {
        string repositoryRoot = FindRepositoryRoot();
        string serviceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "ExportDocManager.Infrastructure",
            "Services",
            "ServerMigration",
            "ServerMigrationService.cs"));
        string transactionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "ExportDocManager.Infrastructure",
            "Services",
            "ServerMigration",
            "ServerMigrationFileTransaction.cs"));

        Assert.Contains("DataEntry(\"Marks\"", serviceSource, StringComparison.Ordinal);
        Assert.Contains("服务器迁移源目录不能包含符号链接或重解析点", serviceSource, StringComparison.Ordinal);
        Assert.Contains("服务器迁移源目录不能是符号链接或重解析点", serviceSource, StringComparison.Ordinal);
        Assert.Contains("服务器迁移源文件不能是符号链接或重解析点", serviceSource, StringComparison.Ordinal);
        Assert.Contains("服务器迁移包包含清单之外的文件", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", transactionSource, StringComparison.Ordinal);
        Assert.Contains("EnumerateFileSystemInfos", transactionSource, StringComparison.Ordinal);
        Assert.Contains("TLS/Certbot 证书", ServerMigrationLayout.StoragePolicy, StringComparison.Ordinal);
        Assert.Contains("唛头图片", ServerMigrationLayout.StoragePolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("Deployment/Certificates", ServerMigrationLayout.StoragePolicy, StringComparison.Ordinal);
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static PendingServerMigrationRestore CreateMarker(
        string packageId,
        string stagingDirectoryName,
        string phase) =>
        new()
        {
            SchemaVersion = ServerMigrationLayout.SchemaVersion,
            PackageId = packageId,
            PackageFileName = "migration.edmmigration",
            StagingDirectoryName = stagingDirectoryName,
            Phase = phase,
            Manifest = new ServerMigrationManifest
            {
                SchemaVersion = ServerMigrationLayout.SchemaVersion,
                PackageId = packageId
            }
        };

    private static string CreateTestRoot(string suffix)
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            ".codex-runtime",
            "server-migration-tests",
            $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "ExportDocManager.sln")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate ExportDocManager.sln.");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
