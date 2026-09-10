using ExportDocManager.Services.Office;
using ExportDocManager.Models;
using Microsoft.AspNetCore.Mvc;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting;

public static partial class ApiEndpointRouteBuilderExtensions
{
    private static void MapMeetingRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        const string resource = PermissionResourceCatalog.OfficeRooms;
        endpoints.MapDelete("/api/office/rooms/{id:int:min(1)}", async (IMeetingRoomService service, int id,
            [FromBody] DeleteRecordRequest request, CancellationToken cancellationToken) =>
        {
            await service.DeleteRoomAsync(id, request, cancellationToken);
            return TypedResults.NoContent();
        }).OfficeEndpoint("DeleteMeetingRoom", resource, PermissionAction.Manage);
        endpoints.MapPut("/api/office/bookings/{id:int:min(1)}", async (IMeetingRoomService service, int id,
            MeetingBookingUpdateRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.UpdateBookingAsync(id, request, cancellationToken)))
            .OfficeEndpoint("UpdateMeetingBooking", resource, PermissionAction.Edit);
        endpoints.MapGet("/api/office/rooms", async (IMeetingRoomService service, string? keyword,
            bool? includeInactive, int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryRoomsAsync(new OfficeResourceQuery(keyword, includeInactive ?? false,
                pageNumber ?? 1, pageSize ?? 24), cancellationToken)))
            .OfficeEndpoint("ListMeetingRooms", resource, PermissionAction.View);

        endpoints.MapPost("/api/office/rooms", async (IMeetingRoomService service, MeetingRoomSaveRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SaveRoomAsync(0, request, cancellationToken)))
            .OfficeEndpoint("CreateMeetingRoom", resource, PermissionAction.Manage);

        endpoints.MapPut("/api/office/rooms/{id:int:min(1)}", async (IMeetingRoomService service, int id, MeetingRoomSaveRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.SaveRoomAsync(id, request, cancellationToken)))
            .OfficeEndpoint("UpdateMeetingRoom", resource, PermissionAction.Manage);

        endpoints.MapGet("/api/office/rooms/{id:int}/availability", async (IMeetingRoomService service, int id,
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.AvailabilityAsync(id, from, to, cancellationToken)))
            .OfficeEndpoint("GetMeetingRoomAvailability", resource, PermissionAction.View);

        endpoints.MapGet("/api/office/bookings", async (IMeetingRoomService service, string? status, bool? mineOnly,
            int? resourceId, DateTimeOffset? from, DateTimeOffset? to, int? pageNumber, int? pageSize, int? requestId, int? applicantUserId, int? employeeId, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.QueryBookingsAsync(new OfficeRequestQuery(status, mineOnly ?? true, resourceId,
                from, to, pageNumber ?? 1, pageSize ?? 20, requestId, applicantUserId, employeeId), cancellationToken)))
            .OfficeEndpoint("ListMeetingBookings", resource, PermissionAction.View);

        endpoints.MapPost("/api/office/bookings", async (IMeetingRoomService service, MeetingBookingCreateRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.CreateBookingAsync(request, cancellationToken)))
            .OfficeEndpoint("CreateMeetingBooking", resource, PermissionAction.Create);

        endpoints.MapGet("/api/office/bookings/{id:int}/history", async (IMeetingRoomService service, int id,
            int? pageNumber, int? pageSize, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.HistoryAsync(id, pageNumber ?? 1, pageSize ?? 50, cancellationToken)))
            .OfficeEndpoint("GetMeetingBookingHistory", resource, PermissionAction.View);

        endpoints.MapPost("/api/office/bookings/{id:int}/approve", async (IMeetingRoomService service, int id, OfficeDecisionRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Approve, request, cancellationToken)))
            .OfficeEndpoint("ApproveMeetingBooking", resource, PermissionAction.Approve);
        endpoints.MapPost("/api/office/bookings/{id:int}/reject", async (IMeetingRoomService service, int id, OfficeDecisionRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Reject, request, cancellationToken)))
            .OfficeEndpoint("RejectMeetingBooking", resource, PermissionAction.Approve);
        endpoints.MapPost("/api/office/bookings/{id:int}/cancel", async (IMeetingRoomService service, int id, OfficeDecisionRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Cancel, request, cancellationToken)))
            .OfficeEndpoint("CancelMeetingBooking", resource, PermissionAction.Cancel);
        endpoints.MapPost("/api/office/bookings/{id:int}/issue-key", async (IMeetingRoomService service, int id, OfficeDecisionRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Issue, request, cancellationToken)))
            .OfficeEndpoint("IssueMeetingRoomKey", resource, PermissionAction.Issue);
        endpoints.MapPost("/api/office/bookings/{id:int}/return-key", async (IMeetingRoomService service, int id, OfficeDecisionRequest request, CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.TransitionAsync(id, OfficeWorkflowAction.Return, request, cancellationToken)))
            .OfficeEndpoint("ReturnMeetingRoomKey", resource, PermissionAction.Return);
    }

    private static RouteHandlerBuilder OfficeEndpoint(this RouteHandlerBuilder endpoint, string name, string resource, string action) =>
        endpoint.WithName(name).WithApiCapability(resource, action)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout);
}
