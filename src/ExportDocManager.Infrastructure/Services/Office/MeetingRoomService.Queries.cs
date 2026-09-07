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
    public Task<PagedResult<MeetingRoomRecord>> QueryRoomsAsync(OfficeResourceQuery query, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, (db, actor, token) =>
        {
            string keyword = Text(query.Keyword, "关键词", 120);
            var rooms = db.MeetingRooms.AsNoTracking().Where(room => room.CompanyScope == actor.CompanyScope && (query.IncludeInactive || room.IsActive) &&
                (keyword == "" || room.Name.Contains(keyword) || room.Location.Contains(keyword)))
                .OrderByDescending(room => room.IsActive).ThenBy(room => room.Name).ThenBy(room => room.Id);
            return PageAsync(Rooms(db, rooms), query.PageNumber, query.PageSize, token);
        }, cancellationToken);

    public Task<IReadOnlyList<MeetingBusySlot>> AvailabilityAsync(int roomId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        office.RunAsync<IReadOnlyList<MeetingBusySlot>>(Resource, PermissionAction.View, false, async (db, actor, token) =>
        {
            from = from.ToUniversalTime();
            to = to.ToUniversalTime();
            if (from >= to || to - from > TimeSpan.FromDays(7) + TimeSpan.FromHours(1))
                throw new ServiceValidationException("可用时段查询须在一周以内。");
            if (!await db.MeetingRooms.AnyAsync(room => room.Id == roomId && room.CompanyScope == actor.CompanyScope, token))
                throw new ResourceNotFoundException("会议室不存在。");
            var now = office.Clock.UtcNow;
            return await db.MeetingBookings.AsNoTracking().Where(item => item.MeetingRoomId == roomId && item.CompanyScope == actor.CompanyScope &&
                    (item.Status == MeetingBookingStatus.Pending || item.Status == MeetingBookingStatus.Approved || item.Status == MeetingBookingStatus.InUse) &&
                    item.StartsAt < to && (item.EndsAt > from || item.Status == MeetingBookingStatus.InUse))
                .OrderBy(item => item.StartsAt).Select(item => new MeetingBusySlot(item.StartsAt,
                    item.Status == MeetingBookingStatus.InUse && item.EndsAt < now ? to : item.EndsAt, item.Status)).ToListAsync(token);
        }, cancellationToken);

    public Task<PagedResult<MeetingBookingRecord>> QueryBookingsAsync(OfficeRequestQuery query, CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, (db, actor, token) =>
        {
            ValidateRequestQuery(query);
            var status = Status<MeetingBookingStatus>(query.Status);
            var from = query.From?.ToUniversalTime();
            var to = query.To?.ToUniversalTime();
            var bookings = office.Requests(db.MeetingBookings.AsNoTracking(), actor, Resource)
                .Where(item => (!query.MineOnly || item.OwnerUserId == actor.Id) && (!status.HasValue || item.Status == status.Value) &&
                    (!query.ResourceId.HasValue || item.MeetingRoomId == query.ResourceId) &&
                    (!query.RequestId.HasValue || item.Id == query.RequestId) && (!query.ApplicantUserId.HasValue || item.OwnerUserId == query.ApplicantUserId) &&
                    (!query.EmployeeId.HasValue || item.EmployeeId == query.EmployeeId) &&
                    (!from.HasValue || item.EndsAt > from.Value) && (!to.HasValue || item.StartsAt < to.Value))
                .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id);
            return PageAsync(BookingRecords(db, bookings), query.PageNumber, query.PageSize, token);
        }, cancellationToken);

    public Task<PagedResult<OfficeRequestEventRecord>> HistoryAsync(int id, int pageNumber = 1, int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        office.RunAsync(Resource, PermissionAction.View, false, async (db, actor, token) =>
        {
            var booking = await db.MeetingBookings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.CompanyScope == actor.CompanyScope, token)
                ?? throw new ResourceNotFoundException("预约不存在。");
            office.DemandRecord(booking, actor, Resource, PermissionAction.View);
            return await ReadEventsAsync(db.OfficeRequestEvents.AsNoTracking().Where(item => item.MeetingBookingId == id && item.CompanyScope == actor.CompanyScope), pageNumber, pageSize, token);
        }, cancellationToken);

    private static IQueryable<MeetingRoomRecord> Rooms(AppDbContext db, IQueryable<MeetingRoom> rooms) =>
        rooms.Select(room =>
            new MeetingRoomRecord(room.Id, room.Name, room.Location, room.Equipment, room.Capacity,
                room.MaximumBookingHours, room.AdvanceBookingDays, room.RequiresKey, room.IsActive,
                db.MeetingBookings.Any(booking => booking.MeetingRoomId == room.Id && booking.Status == MeetingBookingStatus.InUse), room.VersionNumber));

    private static IQueryable<MeetingBookingRecord> BookingRecords(AppDbContext db, IQueryable<MeetingBooking> bookings) =>
        from booking in bookings
        join room in db.MeetingRooms on booking.MeetingRoomId equals room.Id
        select new MeetingBookingRecord(booking.Id, room.Id, room.Name, room.Location, room.RequiresKey,
            booking.OwnerUserId.GetValueOrDefault(), booking.ApplicantName, booking.DepartmentId, booking.Title,
            booking.AttendeeCount, booking.StartsAt, booking.EndsAt, booking.Status, booking.IssuedAt, booking.ReturnedAt,
            booking.CreatedAt, booking.VersionNumber);

    private static Task<MeetingBookingRecord> BookingRecordAsync(AppDbContext db, User actor, int id, CancellationToken token) =>
        BookingRecords(db, db.MeetingBookings.AsNoTracking().Where(item => item.CompanyScope == actor.CompanyScope && item.Id == id)).SingleAsync(token);
}
