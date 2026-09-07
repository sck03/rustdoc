using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapPersonnelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        const string resource = PermissionResourceCatalog.OfficePeople;
        endpoints.MapGet("/api/office/people", async (IPersonnelService service, string? keyword, string? departmentId,
            string? status, bool? attentionOnly, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryAsync(new(keyword, departmentId, status, attentionOnly ?? false,
                pageNumber ?? 1, pageSize ?? 24), cancellationToken)))
            .OfficeEndpoint("ListPersonnel", resource, PermissionAction.View);

        endpoints.MapGet("/api/office/people/options", async (IPersonnelService service, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.OptionsAsync(cancellationToken)))
            .OfficeEndpoint("GetPersonnelOptions", resource, PermissionAction.View);

        endpoints.MapGet("/api/office/people/{id:int:min(1)}", async (IPersonnelService service, int id, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAsync(id, cancellationToken)))
            .OfficeEndpoint("GetPersonnel", resource, PermissionAction.ViewDetails);

        endpoints.MapPost("/api/office/people", async (IPersonnelService service, PersonnelCreateRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.CreateAsync(request, cancellationToken)))
            .OfficeEndpoint("CreatePersonnel", resource, PermissionAction.Create);

        endpoints.MapPut("/api/office/people/{id:int:min(1)}", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelUpdateRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.UpdateAsync(id, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("UpdatePersonnel", resource, PermissionAction.Edit);

        endpoints.MapGet("/api/office/people/{id:int:min(1)}/history", async (IPersonnelService service, int id,
            int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.HistoryAsync(id, pageNumber ?? 1, pageSize ?? 24, cancellationToken)))
            .OfficeEndpoint("GetPersonnelHistory", resource, PermissionAction.ViewDetails);

        endpoints.MapGet("/api/office/people/{id:int:min(1)}/clearance", async (IPersonnelService service, int id, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ClearanceAsync(id, cancellationToken)))
            .OfficeEndpoint("GetPersonnelClearance", resource, PermissionAction.ViewDetails);

        endpoints.MapGet("/api/office/people/{id:int:min(1)}/accounts", async (IPersonnelService service, int id,
            string? keyword, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.AccountOptionsAsync(id, keyword, pageNumber ?? 1, pageSize ?? 24, cancellationToken)))
            .OfficeEndpoint("ListPersonnelAccountOptions", resource, PermissionAction.Assign);

        endpoints.MapPost("/api/office/people/{id:int:min(1)}/account", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelAccountRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.LinkAccountAsync(id, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("LinkPersonnelAccount", resource, PermissionAction.Assign);

        endpoints.MapPost("/api/office/people/{id:int:min(1)}/confirm", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelTransitionRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.TransitionAsync(id, PersonnelAction.Confirm, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("ConfirmPersonnel", resource, PermissionAction.Transition);
        endpoints.MapPost("/api/office/people/{id:int:min(1)}/transfer", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelTransitionRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.TransitionAsync(id, PersonnelAction.Transfer, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("TransferPersonnel", resource, PermissionAction.Transition);
        endpoints.MapPost("/api/office/people/{id:int:min(1)}/depart", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelTransitionRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.TransitionAsync(id, PersonnelAction.Depart, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("DepartPersonnel", resource, PermissionAction.Transition);
        endpoints.MapPost("/api/office/people/{id:int:min(1)}/rehire", (IPersonnelService service, IApiSessionTokenService sessions,
            int id, PersonnelTransitionRequest request, CancellationToken cancellationToken) =>
            CompletePersonnelChangeAsync(service.TransitionAsync(id, PersonnelAction.Rehire, request, cancellationToken), sessions, cancellationToken))
            .OfficeEndpoint("RehirePersonnel", resource, PermissionAction.Transition);
    }

    private static async Task<Ok<PersonnelRecord>> CompletePersonnelChangeAsync(Task<PersonnelChangeResult> operation,
        IApiSessionTokenService sessions, CancellationToken token)
    {
        var result = await operation;
        if (result.ChangedAccountUserId is int userId) await sessions.RevokeUserSessionsAsync(userId, token);
        return TypedResults.Ok(result.Record);
    }
}
