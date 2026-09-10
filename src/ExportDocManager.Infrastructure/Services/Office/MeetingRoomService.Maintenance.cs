using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class MeetingRoomService
{
    public Task DeleteRoomAsync(int id, DeleteRecordRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Manage, true, async (db, actor, token) =>
        {
            var room = await LockRoomAsync(db, id, actor, token);
            Version(request.ExpectedVersion, room.VersionNumber);
            string reason = Text(request.Reason, "删除原因", 500, true);
            if (await db.MeetingBookings.AnyAsync(item => item.MeetingRoomId == id, token))
                throw new ResourceConflictException("会议室已有预约记录，不能删除；可在处理完交接后停用。");
            RecordDeletionAudit.Add(db, actor, nameof(MeetingRoom), id, reason, office.Clock.UtcNow);
            db.MeetingRooms.Remove(room);
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

    public Task<MeetingBookingRecord> UpdateBookingAsync(int id, MeetingBookingUpdateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Edit, true, async (db, actor, token) =>
        {
            var snapshot = await db.MeetingBookings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("预约不存在。");
            office.DemandRecord(snapshot, actor, Resource, PermissionAction.Edit);
            var room = await LockRoomAsync(db, snapshot.MeetingRoomId, actor, token);
            var booking = await db.MeetingBookings.SingleAsync(item => item.Id == id, token);
            Version(request.ExpectedVersion, booking.VersionNumber);
            if (booking.Status is not (MeetingBookingStatus.Pending or MeetingBookingStatus.Approved))
                throw new ResourceConflictException("仅尚未交接的预约可以修改；已结束记录保留历史。");
            var start = request.StartsAt.ToUniversalTime();
            var end = request.EndsAt.ToUniversalTime();
            string title = Text(request.Title, "会议主题", 200, true);
            await ValidateBookingAsync(db, room, request.AttendeeCount, start, end, id, token);
            if (booking.Title == title && booking.AttendeeCount == request.AttendeeCount && booking.StartsAt == start && booking.EndsAt == end)
                return await BookingRecordAsync(db, actor, id, token);
            booking.Title = title;
            booking.AttendeeCount = request.AttendeeCount;
            booking.StartsAt = start;
            booking.EndsAt = end;
            booking.Status = office.IsLocalRegister ? MeetingBookingStatus.Approved : MeetingBookingStatus.Pending;
            office.AddEvent(db, actor, "Edit", office.IsLocalRegister ? "修正预约登记" : "修改预约后重新审批", bookingId: id);
            await db.SaveChangesAsync(token);
            return await BookingRecordAsync(db, actor, id, token);
        }, cancellationToken);

    private async Task ValidateBookingAsync(AppDbContext db, MeetingRoom room, int count, DateTimeOffset start,
        DateTimeOffset end, int excludedId, CancellationToken token)
    {
        if (!room.IsActive) throw new ResourceConflictException("会议室已停用。");
        Range(count, 1, room.Capacity, "参会人数");
        var now = office.Clock.UtcNow;
        if (start < now || start > now.AddDays(room.AdvanceBookingDays) || end - start < TimeSpan.FromMinutes(15) ||
            end - start > TimeSpan.FromHours(room.MaximumBookingHours))
            throw new ServiceValidationException($"请预约未来 {room.AdvanceBookingDays} 天内的时段，时长为 15 分钟至 {room.MaximumBookingHours} 小时。");
        await DemandAvailableAsync(db, room.Id, start, end, excludedId, token);
    }
}
