namespace ExportDocManager.Infrastructure.Tests;

public sealed class GitHubWorkflowSummaryContractTests
{
    [Fact]
    public void DesktopPackageSummary_ShouldUseLiteralHereStringForMarkdownBackticks()
    {
        string workflow = File.ReadAllText(
            ResolveWorkspacePath(".github", "workflows", "desktop-package-reusable.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("- name: Write package summary", workflow, StringComparison.Ordinal);
        Assert.Contains("          @'\n          ## ExportDocManager 桌面包", workflow, StringComparison.Ordinal);
        Assert.Contains("          '@ | Out-File -FilePath $env:GITHUB_STEP_SUMMARY", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("          @\"\n          ## ExportDocManager 桌面包", workflow, StringComparison.Ordinal);
    }

    private static string ResolveWorkspacePath(params string[] segments)
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(new[] { directory }.Concat(segments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate {string.Join("/", segments)} from test output.");
    }
}
