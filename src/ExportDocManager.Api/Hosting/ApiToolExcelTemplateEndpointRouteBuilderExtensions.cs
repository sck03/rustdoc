using ExportDocManager.Services.Data;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.Reporting;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static void MapExcelTemplateExportEndpoint(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPost("/api/tools/excel/template/save-to-path", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                ApiDesktopAccessOptions desktopAccessOptions,
                ApiBackgroundJobRunner jobRunner,
                ApiExcelOutputRequest request) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                if (!ApiEndpointAuth.HasValidDesktopAccess(context, desktopAccessOptions))
                {
                    return WriteForbidden("服务器路径保存仅允许可信 Tauri 桌面端；浏览器请使用 Excel 下载任务。");
                }

                var validation = ValidateExcelDestinationPath(
                    request?.DestinationPath,
                    "Excel 模板输出路径",
                    out string destinationPath);
                if (validation != null)
                {
                    return validation;
                }

                return AcceptedBackgroundJob(EnqueueExcelTemplateExportJob(jobRunner, user.Username, destinationPath));
            })
            .WithName("StartExcelTemplateSaveToPathJob");

            endpoints.MapPost("/api/tools/excel/template/download", (
                HttpContext context,
                IApiSessionTokenService tokenService,
                IAppPathProvider pathProvider,
                ApiBackgroundJobRunner jobRunner) =>
            {
                var user = ApiEndpointAuth.RequireUser(context, tokenService);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                string destinationPath = CreateBrowserDownloadPath(
                    pathProvider,
                    "ExcelTemplate",
                    "导入数据模板.xlsx");
                return AcceptedBackgroundJob(EnqueueExcelTemplateExportJob(
                    jobRunner,
                    user.Username,
                    destinationPath));
            })
            .WithName("StartExcelTemplateDownloadJob");
        }

        internal static BackgroundJobSnapshot EnqueueExcelTemplateExportJob(
            ApiBackgroundJobRunner jobRunner,
            string requestedBy,
            string destinationPath)
        {
            return jobRunner.Enqueue(
                "ExcelTemplateExport",
                "导出 Excel 导入模板",
                requestedBy,
                async (provider, jobContext) =>
                {
                    jobContext.Report(10, "正在准备 Excel 模板", "读取随程序目录 Resources/ExcelTemplates。", destinationPath);
                    await provider.GetRequiredService<ISettingsService>().LoadAsync();
                    jobContext.CancellationToken.ThrowIfCancellationRequested();

                    var templateService = provider.GetRequiredService<IExcelImportTemplateService>();
                    string outputPath = await Task.Run(
                        () => templateService.ExportDefaultTemplate(destinationPath, overwrite: true),
                        jobContext.CancellationToken);

                    jobContext.Report(95, "正在保存 Excel 模板", Path.GetFileName(outputPath), outputPath);
                    return outputPath;
                },
                retryOperation: "StartExcelTemplateExportJob",
                retryRequestJson: SerializeBackgroundJobRetryRequest(new ApiExcelOutputRequest
                {
                    DestinationPath = destinationPath
                }),
                initialOutputPath: destinationPath);
        }
    }
}
