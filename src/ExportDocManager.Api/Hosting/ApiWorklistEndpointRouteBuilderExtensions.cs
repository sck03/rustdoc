using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Worklist;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapWorklistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/worklist", async (IServiceProvider services, string? source, WorklistDueFilter? due,
            int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await (services.GetService<IWorklistService>() ??
                throw new UserVisibleInfrastructureException("当前产品未安装或已关闭个人待办模块。"))
                .QueryAsync(new WorklistQuery(source, due ?? WorklistDueFilter.All, pageNumber ?? 1, pageSize ?? 20), cancellationToken)))
            .WithName("GetWorklist").AllowApiWithoutPermission();
    }
}
