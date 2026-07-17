using ExportDocManager.Api.Hosting;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Tests
{
    public class ApiSingleWindowPackagePathHelperTests
    {
        [Fact]
        public void ResolveSingleWindowImportWorkingRoot_WithoutRequestedPath_UsesRuntimeSingleWindowInbox()
        {
            using var scope = TempRuntimeScope.Create();

            string resolved = ApiEndpointRouteBuilderExtensions.ResolveSingleWindowImportWorkingRoot(
                scope.PathProvider,
                SingleWindowPackageType.SubmitPackage,
                string.Empty);

            Assert.Equal(
                Path.Combine(scope.PathProvider.SingleWindowRoot, "Inbox"),
                resolved);
            Assert.StartsWith(scope.PathProvider.SingleWindowRoot, resolved, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveSingleWindowImportWorkingRoot_WithRelativePath_StaysUnderRuntimeSingleWindowRoot()
        {
            using var scope = TempRuntimeScope.Create();

            string resolved = ApiEndpointRouteBuilderExtensions.ResolveSingleWindowImportWorkingRoot(
                scope.PathProvider,
                SingleWindowPackageType.ReceiptPackage,
                Path.Combine("ReceiptInbox", "manual"));

            Assert.Equal(
                Path.Combine(scope.PathProvider.SingleWindowRoot, "ReceiptInbox", "manual"),
                resolved);
            Assert.StartsWith(scope.PathProvider.SingleWindowRoot, resolved, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveSingleWindowImportWorkingRoot_WithAbsolutePathOutsideRuntimeRoot_RejectsPath()
        {
            using var scope = TempRuntimeScope.Create();
            string outsideRoot = Path.Combine(scope.DataRoot, "outside-single-window");

            var error = Assert.Throws<UnauthorizedAccessException>(() =>
                ApiEndpointRouteBuilderExtensions.ResolveSingleWindowImportWorkingRoot(
                    scope.PathProvider,
                    SingleWindowPackageType.SubmitPackage,
                    outsideRoot));

            Assert.Contains("SingleWindow", error.Message, StringComparison.Ordinal);
        }

        private sealed class TempRuntimeScope : IDisposable
        {
            private TempRuntimeScope(string root)
            {
                Root = root;
                AppRoot = Path.Combine(root, "app");
                DataRoot = Path.Combine(root, "data");
                PathProvider = new RuntimeAppPathProvider(AppRoot, DataRoot);
            }

            public string Root { get; }

            public string AppRoot { get; }

            public string DataRoot { get; }

            public RuntimeAppPathProvider PathProvider { get; }

            public static TempRuntimeScope Create()
            {
                string root = Path.Combine(Path.GetTempPath(), "ExportDocManager.Tests", Guid.NewGuid().ToString("N"));
                return new TempRuntimeScope(root);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup for test temp folders.
                }
            }
        }
    }
}
