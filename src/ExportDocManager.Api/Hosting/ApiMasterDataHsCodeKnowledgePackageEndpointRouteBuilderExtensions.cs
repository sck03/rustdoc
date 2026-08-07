using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapHsCodeKnowledgePackageEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/hs-knowledge/export", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiAuthorizationService authorizationService,
                IHsCodeKnowledgeService service,
                IAppPathProvider pathProvider,
                DateTimeOffset? since,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!authorizationService.CanUseModule(
                        user,
                        PermissionModuleCatalog.DocumentHsKnowledge,
                        PermissionAccessLevel.Manage))
                {
                    return WriteForbidden("只有管理权限可以导出共享 HS 知识包。");
                }

                string exportDirectory = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "HsKnowledgeExports",
                    "knowledge-export");
                string fileName = $"ExportDocManager-HsLibrary-{DateTime.Now:yyyyMMdd}.edmhs";
                string outputPath = Path.Combine(exportDirectory, fileName);
                try
                {
                    await using (var output = new FileStream(
                                     outputPath,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     bufferSize: 128 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await service.ExportPackageAsync(output, since, cancellationToken);
                    }

                    return StreamTemporaryFile(
                        context,
                        outputPath,
                        "application/vnd.exportdocmanager.hs-knowledge+zip",
                        fileName,
                        exportDirectory);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AtomicFileHelper.TryDeleteDirectory(exportDirectory);
                    throw;
                }
                catch (Exception ex)
                {
                    AtomicFileHelper.TryDeleteDirectory(exportDirectory);
                    return WriteServiceException(ex);
                }
            }).WithName("ExportHsCodeKnowledge");

            endpoints.MapPost("/api/master-data/hs-knowledge/import", async (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IHsCodeKnowledgeService service,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {
                if (ApiEndpointAuth.RequireUser(context, tokenService) == null)
                {
                    return Results.Unauthorized();
                }

                string tempRoot = RuntimeCachePathHelper.CreateUniqueDirectory(
                    pathProvider,
                    "HsKnowledgeImports",
                    "knowledge-import");
                try
                {
                    string path = Path.Combine(tempRoot, "library.edmhs");
                    await using (var output = File.Create(path))
                    {
                        await ApiUploadLimits.CopyRequestBodyAsync(
                            context.Request,
                            output,
                            ApiUploadLimits.PackageImportBytes,
                            cancellationToken);
                    }

                    var preview = await service.PreviewPackageAsync(path, cancellationToken);
                    var result = await service.ImportPackageAsync(preview, cancellationToken);
                    return Results.Ok(new
                    {
                        preview.FileName,
                        preview.HsCodeCount,
                        preview.ExampleCount,
                        preview.ReplacementCount,
                        preview.FeedbackCount,
                        preview.Warnings,
                        result
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
                {
                    return WriteServiceException(ex);
                }
                finally
                {
                    AtomicFileHelper.TryDeleteDirectory(tempRoot);
                }
            }).WithName("ImportHsCodeKnowledge");
        }
    }
}
