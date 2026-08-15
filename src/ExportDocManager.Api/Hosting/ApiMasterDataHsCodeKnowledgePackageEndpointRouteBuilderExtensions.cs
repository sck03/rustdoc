using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapHsCodeKnowledgePackageEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/master-data/hs-knowledge/export", async (
                HttpContext context,
                ApiAuthorizationService authorizationService,
                IHsCodeKnowledgeService service,
                IAppPathProvider pathProvider,
                IBusinessClock clock,
                DateTimeOffset? since,
                CancellationToken cancellationToken) =>
            {
                var user = ApiEndpointAuth.GetRequiredUser(context);

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
                string fileName = $"ExportDocManager-HsLibrary-{clock.Now:yyyyMMdd}.edmhs";
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
            }).WithName("ExportHsCodeKnowledge")
            .WithApiPermission(
                PermissionModuleCatalog.DocumentHsKnowledge,
                readAccessLevel: PermissionAccessLevel.Manage)
            .Produces<byte[]>(StatusCodes.Status200OK, "application/vnd.exportdocmanager.hs-knowledge+zip")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            endpoints.MapPost("/api/master-data/hs-knowledge/import", async (
                HttpContext context,
                IHsCodeKnowledgeService service,
                IAppPathProvider pathProvider,
                CancellationToken cancellationToken) =>
            {

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
                            ApiUploadLimits.HsCodeKnowledgePackageBytes,
                            cancellationToken);
                    }

                    var preview = await service.PreviewPackageAsync(path, cancellationToken);
                    var result = await service.ImportPackageAsync(preview, cancellationToken);
                    return Results.Ok(new ApiHsCodeKnowledgeImportResponse(
                        preview.FileName,
                        preview.HsCodeCount,
                        preview.ExampleCount,
                        preview.ReplacementCount,
                        preview.FeedbackCount,
                        preview.Warnings,
                        result));
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
            }).Accepts<IFormFile>("application/octet-stream").WithName("ImportHsCodeKnowledge")
            .Produces<ApiHsCodeKnowledgeImportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
        }
    }
}
