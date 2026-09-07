using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class MeetingRoomService(OfficeServiceContext office) : IMeetingRoomService
{
    private const string Resource = PermissionResourceCatalog.OfficeRooms;

    public Task<MeetingRoomRecord> SaveRoomAsync(int id, MeetingRoomSaveRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Manage, true, async (db, actor, token) =>
        {
            Range(id, 0, int.MaxValue, "会议室编号");
            Range(request.Capacity, 1, 10000, "容纳人数");
            Range(request.MaximumBookingHours, 1, 24, "最长预约小时数");
            Range(request.AdvanceBookingDays, 1, 365, "提前预约天数");
            var room = id == 0 ? new MeetingRoom { CompanyScope = actor.CompanyScope! } : await LockRoomAsync(db, id, actor, token);
            if (id == 0)
            {
                if (request.ExpectedVersion != 0) throw new ServiceValidationException("新增会议室不能包含已有版本。");
                db.MeetingRooms.Add(room);
            }
            else
            {
                Version(request.ExpectedVersion, room.VersionNumber);
                if (request.Capacity < room.Capacity && await db.MeetingBookings.AnyAsync(item => item.MeetingRoomId == id &&
                    item.AttendeeCount > request.Capacity && (item.Status == MeetingBookingStatus.InUse || item.EndsAt > office.Clock.UtcNow &&
                        (item.Status == MeetingBookingStatus.Pending || item.Status == MeetingBookingStatus.Approved)), token))
                    throw new ResourceConflictException("新容量不能少于未结束预约的参会人数，请先处理相关预约。");
                if ((!request.IsActive || request.RequiresKey != room.RequiresKey) &&
                    await db.MeetingBookings.AnyAsync(item => item.MeetingRoomId == id &&
                        (item.Status == MeetingBookingStatus.InUse || item.EndsAt > office.Clock.UtcNow &&
                            (item.Status == MeetingBookingStatus.Pending || item.Status == MeetingBookingStatus.Approved)), token))
                    throw new ResourceConflictException("会议室仍有预约或未归还的交接，请处理后再停用或更改钥匙规则。");
            }
            room.Name = Text(request.Name, "会议室名称", 120, true);
            room.Location = Text(request.Location, "位置", 200);
            room.Equipment = Text(request.Equipment, "设备说明", 500);
            room.Capacity = request.Capacity;
            room.MaximumBookingHours = request.MaximumBookingHours;
            room.AdvanceBookingDays = request.AdvanceBookingDays;
            room.RequiresKey = request.RequiresKey;
            room.IsActive = request.IsActive;
            await db.SaveChangesAsync(token);
            return await Rooms(db, db.MeetingRooms.AsNoTracking().Where(item => item.Id == room.Id)).SingleAsync(token);
        }, cancellationToken);

    public Task<MeetingBookingRecord> CreateBookingAsync(MeetingBookingCreateRequest request, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.Create, true, async (db, actor, token) =>
        {
            var applicant = await office.ApplicantAsync(db, actor, request.EmployeeId, token);
            RequestKey(request.RequestKey);
            string title = Text(request.Title, "会议主题", 200, true);
            DateTimeOffset start = request.StartsAt.ToUniversalTime(), end = request.EndsAt.ToUniversalTime();
            var room = await LockRoomAsync(db, request.MeetingRoomId, actor, token);
            var previous = await db.MeetingBookings.SingleOrDefaultAsync(item => item.CompanyScope == actor.CompanyScope &&
                item.OwnerUserId == actor.Id && item.RequestKey == request.RequestKey, token);
            if (previous != null)
            {
                if (previous.MeetingRoomId != room.Id || previous.Title != title || previous.AttendeeCount != request.AttendeeCount ||
                    previous.StartsAt != start || previous.EndsAt != end || previous.EmployeeId != request.EmployeeId)
                    throw new ResourceConflictException("同一请求标识不能用于不同的预约内容。");
                return await BookingRecordAsync(db, actor, previous.Id, token);
            }
            if (!room.IsActive) throw new ResourceConflictException("会议室已停用。");
            Range(request.AttendeeCount, 1, room.Capacity, "参会人数");
            var now = office.Clock.UtcNow;
            if (start < now || start > now.AddDays(room.AdvanceBookingDays) || end - start < TimeSpan.FromMinutes(15) ||
                end - start > TimeSpan.FromHours(room.MaximumBookingHours))
                throw new ServiceValidationException($"请预约未来 {room.AdvanceBookingDays} 天内的时段，时长为 15 分钟至 {room.MaximumBookingHours} 小时。");
            await DemandAvailableAsync(db, room.Id, start, end, 0, token);
            var booking = new MeetingBooking
            {
                MeetingRoomId = room.Id,
                EmployeeId = request.EmployeeId,
                RequestKey = request.RequestKey,
                CompanyScope = actor.CompanyScope!,
                DepartmentId = applicant.Department,
                OwnerUserId = actor.Id,
                ApplicantName = applicant.Name,
                Status = office.IsLocalRegister ? MeetingBookingStatus.Approved : MeetingBookingStatus.Pending,
                Title = title,
                AttendeeCount = request.AttendeeCount,
                StartsAt = start,
                EndsAt = end,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MeetingBookings.Add(booking);
            await db.SaveChangesAsync(token);
            office.AddEvent(db, actor, office.IsLocalRegister ? "Register" : "Submit", "", bookingId: booking.Id);
            await db.SaveChangesAsync(token);
            return await BookingRecordAsync(db, actor, booking.Id, token);
        }, cancellationToken);

    public Task<MeetingBookingRecord> TransitionAsync(int id, OfficeWorkflowAction action, OfficeDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, ActionPermission(action), true, async (db, actor, token) =>
        {
            var snapshot = await db.MeetingBookings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("预约不存在。");
            office.DemandRecord(snapshot, actor, Resource, ActionPermission(action));
            var room = await LockRoomAsync(db, snapshot.MeetingRoomId, actor, token);
            var booking = await db.MeetingBookings.SingleAsync(item => item.Id == id, token);
            Version(request.ExpectedVersion, booking.VersionNumber);
            string note = Text(request.Note, "处理说明", 500, action is OfficeWorkflowAction.Reject or OfficeWorkflowAction.Cancel);
            var now = office.Clock.UtcNow;
            switch (action)
            {
                case OfficeWorkflowAction.Approve:
                case OfficeWorkflowAction.Reject:
                    DemandOtherApplicant(booking, actor);
                    RequireStatus(booking, MeetingBookingStatus.Pending);
                    if (action == OfficeWorkflowAction.Approve)
                    {
                        if (!room.IsActive || booking.EndsAt <= now || booking.AttendeeCount > room.Capacity)
                            throw new ResourceConflictException("预约已过期、会议室已停用或容量不足，请驳回后重新申请。");
                        await DemandAvailableAsync(db, room.Id, booking.StartsAt, booking.EndsAt, id, token);
                    }
                    booking.Status = action == OfficeWorkflowAction.Approve ? MeetingBookingStatus.Approved : MeetingBookingStatus.Rejected;
                    break;
                case OfficeWorkflowAction.Cancel:
                    if (booking.Status is not (MeetingBookingStatus.Pending or MeetingBookingStatus.Approved))
                        throw new ResourceConflictException("仅待审批或尚未交接的预约可以取消；已领钥匙须先归还。");
                    booking.Status = MeetingBookingStatus.Cancelled;
                    break;
                case OfficeWorkflowAction.Issue:
                    RequireStatus(booking, MeetingBookingStatus.Approved);
                    if (!room.IsActive || now < booking.StartsAt.AddMinutes(-30) || now >= booking.EndsAt)
                        throw new ResourceConflictException("请在预约开始前 30 分钟至结束前办理交接。");
                    if (await db.MeetingBookings.AnyAsync(item => item.MeetingRoomId == room.Id && item.Status == MeetingBookingStatus.InUse, token))
                        throw new ResourceConflictException("上一场使用尚未结束／钥匙尚未归还，请先核实归还。");
                    booking.Status = MeetingBookingStatus.InUse;
                    booking.IssuedAt = now;
                    break;
                case OfficeWorkflowAction.Return:
                    RequireStatus(booking, MeetingBookingStatus.InUse);
                    booking.Status = MeetingBookingStatus.Completed;
                    booking.ReturnedAt = now;
                    break;
                default: throw new ServiceValidationException("未知预约操作。");
            }
            office.AddEvent(db, actor, action.ToString(), note, bookingId: id);
            await db.SaveChangesAsync(token);
            return await BookingRecordAsync(db, actor, id, token);
        }, cancellationToken);

    private static async Task DemandAvailableAsync(AppDbContext db, int roomId, DateTimeOffset start,
        DateTimeOffset end, int excludedId, CancellationToken token)
    {
        if (await db.MeetingBookings.AnyAsync(item => item.MeetingRoomId == roomId && item.Id != excludedId &&
            (item.Status == MeetingBookingStatus.Pending || item.Status == MeetingBookingStatus.Approved || item.Status == MeetingBookingStatus.InUse) &&
            item.StartsAt < end && item.EndsAt > start, token))
            throw new ResourceConflictException("所选时段已有预约（含待审批申请），请选择其他时间或会议室。");
    }

    private static void RequireStatus(MeetingBooking booking, MeetingBookingStatus required)
    {
        if (booking.Status != required) throw new ResourceConflictException("当前预约状态不允许此操作，请刷新后核对。");
    }
}
