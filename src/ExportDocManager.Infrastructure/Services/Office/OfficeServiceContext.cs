using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Security;
using ExportDocManager.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Office;

/// <summary>Shared transaction, timeout and company boundary for office operations.</summary>
public sealed class OfficeServiceContext(
    IDbContextFactory<AppDbContext> factory, BusinessDataAccessScope access, IBusinessClock clock, OfficeOperatingMode mode)
{
    public IBusinessClock Clock => clock;
    public bool IsLocalRegister => mode == OfficeOperatingMode.LocalRegister;
    public bool HasPermission(string resource, string action, User user) => access.HasPermission(resource, action, user);
    public bool CanRecord(IBusinessOwnedEntity record, User user, string resource, string action) =>
        record.CompanyScope == user.CompanyScope && access.CanAccessOwnedBusinessRecord(
            record.OwnerUserId, record.DepartmentId, record.CompanyScope, resource, action, user);

    public async Task<T> RunAsync<T>(string resource, string action, bool write,
        Func<AppDbContext, User, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (mode == OfficeOperatingMode.Disabled || (mode == OfficeOperatingMode.Team) != access.UsesPostgreSql)
            throw new PermissionDeniedException("公司行政功能需要团队模式或单机行政版。");
        var user = access.CurrentUser;
        if (user is not { Id: > 0, IsActive: true } || string.IsNullOrWhiteSpace(user.CompanyScope))
            throw new PermissionDeniedException("请先在账号与权限中为当前账号设置所属公司。");
        access.DemandPermission(resource, action, user);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        async Task<T> Execute(AppDbContext db, CancellationToken token)
        {
            if (!await db.OrganizationCompanies.AnyAsync(company => company.Code == user.CompanyScope && company.IsActive, token))
                throw new PermissionDeniedException("当前账号所属公司不存在或已停用。");
            return await operation(db, user, token);
        }
        try
        {
            if (write) return await AppDbContextExecution.ExecuteInTransactionAsync(factory, Execute, timeout.Token);
            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            return await Execute(db, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new ServiceTimeoutException("公司行政操作超时，请刷新后核对结果再重试。", exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ServiceConcurrencyException("记录已被其他人处理，请刷新后重试。", exception);
        }
        catch (Exception exception) when (RelationalExceptionClassifier.IsWriteContention(exception))
        {
            throw new ServiceConcurrencyException("资源正在被其他人处理，请刷新后重试。", exception);
        }
        catch (DbUpdateException exception) when (RelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        {
            throw new ResourceConflictException("同公司中已存在同名资源或相同请求，请刷新后核对。", exception);
        }
    }

    public IQueryable<T> Requests<T>(IQueryable<T> query, User user, string resource, string action = PermissionAction.View)
        where T : class, IBusinessOwnedEntity =>
        access.ApplyBusinessScope(query.Where(item => item.CompanyScope == user.CompanyScope), resource, action, user);

    public async Task<(string Name, string Department)> ApplicantAsync(AppDbContext db, User actor, int? employeeId, CancellationToken token)
    {
        await OfficeAccountAccess.DemandActiveApplicantAsync(db, actor, token);
        if (!IsLocalRegister)
        {
            if (employeeId.HasValue) throw new PermissionDeniedException("团队模式须由员工本人提交申请。");
            return (ActorName(actor), actor.DepartmentId ?? string.Empty);
        }

        if (employeeId is not > 0) throw new ServiceValidationException("请选择需要登记的人员。");
        var employee = await (db.Database.IsNpgsql()
            ? db.PersonnelEmployees.FromSqlInterpolated($"SELECT * FROM \"PersonnelEmployees\" WHERE \"Id\" = {employeeId} AND \"CompanyScope\" = {actor.CompanyScope} FOR SHARE")
            : db.PersonnelEmployees.Where(item => item.Id == employeeId && item.CompanyScope == actor.CompanyScope))
            .AsNoTracking().SingleOrDefaultAsync(token);
        if (employee == null || employee.Status == EmploymentStatus.Departed)
            throw new ServiceValidationException("请选择本公司在职人员；离职人员不能新增预约或领用记录。");
        return (employee.FullName, employee.DepartmentId);
    }

    public void DemandRecord(IBusinessOwnedEntity record, User user, string resource, string action)
    {
        if (record.CompanyScope != user.CompanyScope)
            throw new PermissionDeniedException("不能访问其他公司的记录。");
        access.DemandRecordAccess(record, resource, action);
    }

    public static async Task<MeetingRoom> LockRoomAsync(AppDbContext db, int id, User user, CancellationToken token) =>
        await (db.Database.IsNpgsql()
            ? db.MeetingRooms.FromSqlInterpolated($"SELECT * FROM \"MeetingRooms\" WHERE \"Id\" = {id} AND \"CompanyScope\" = {user.CompanyScope} FOR UPDATE")
            : db.MeetingRooms.Where(item => item.Id == id && item.CompanyScope == user.CompanyScope))
            .SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("会议室不存在。");

    public static async Task<OfficeSupply> LockSupplyAsync(AppDbContext db, int id, User user, CancellationToken token) =>
        await (db.Database.IsNpgsql()
            ? db.OfficeSupplies.FromSqlInterpolated($"SELECT * FROM \"OfficeSupplies\" WHERE \"Id\" = {id} AND \"CompanyScope\" = {user.CompanyScope} FOR UPDATE")
            : db.OfficeSupplies.Where(item => item.Id == id && item.CompanyScope == user.CompanyScope))
            .SingleOrDefaultAsync(token) ?? throw new ResourceNotFoundException("物品不存在。");

    public static void Version(int expected, int actual)
    {
        if (expected <= 0 || expected != actual)
            throw new ServiceConcurrencyException("记录已更新，请刷新后重新操作。");
    }

    public static string Text(string? value, string label, int maximum, bool required = false)
    {
        string clean = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        if (clean.Length > maximum || required && clean.Length == 0 || clean.Any(ch => char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t'))
            throw new ServiceValidationException($"{label}必须为{(required ? "1" : "0")}—{maximum}个字符，且不能包含控制字符。");
        return clean;
    }

    public static void Range(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum) throw new ServiceValidationException($"{label}须在 {minimum}—{maximum} 之间。");
    }

    public static void RequestKey(Guid key)
    {
        if (key == Guid.Empty) throw new ServiceValidationException("请求标识不能为空。");
    }

    public static string ActionPermission(OfficeWorkflowAction action) => action switch
    {
        OfficeWorkflowAction.Approve or OfficeWorkflowAction.Reject => PermissionAction.Approve,
        OfficeWorkflowAction.Cancel => PermissionAction.Cancel,
        OfficeWorkflowAction.Issue => PermissionAction.Issue,
        OfficeWorkflowAction.Return => PermissionAction.Return,
        _ => throw new ServiceValidationException("未知申请操作。")
    };

    public static void DemandOtherApplicant(IBusinessOwnedEntity request, User actor)
    {
        if (request.OwnerUserId == actor.Id) throw new PermissionDeniedException("不能审批自己的申请，请由另一位获授权的管理员审批。");
    }

    public void AddEvent(AppDbContext db, User actor, string action, string note, int? bookingId = null, int? requestId = null, int quantity = 0) =>
        db.OfficeRequestEvents.Add(new OfficeRequestEvent
        {
            CompanyScope = actor.CompanyScope!,
            ActorUserId = actor.Id,
            ActorName = ActorName(actor),
            Action = action,
            Quantity = quantity,
            Note = note,
            MeetingBookingId = bookingId,
            OfficeSupplyRequestId = requestId,
            CreatedAt = clock.UtcNow
        });

    public static string ActorName(User user) => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;

    public static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> query, int page, int size, CancellationToken token)
    {
        Range(page, 1, 1000000, "页码");
        Range(size, 1, 100, "每页条数");
        return new PagedResult<T>(await query.Skip((page - 1) * size).Take(size).ToListAsync(token),
            await query.CountAsync(token), page, size);
    }

    public static Task<PagedResult<OfficeRequestEventRecord>> ReadEventsAsync(IQueryable<OfficeRequestEvent> query, int page, int size, CancellationToken token) =>
        PageAsync(query.OrderByDescending(item => item.Id).Select(item => new OfficeRequestEventRecord(
            item.Id, item.Action, item.Quantity, item.ActorName, item.Note, item.CreatedAt)), page, size, token);

    public static T? Status<T>(string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string? name = Enum.GetNames<T>().FirstOrDefault(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name == null ? throw new ServiceValidationException("申请状态无效。") : Enum.Parse<T>(name);
    }

    public static void ValidateRequestQuery(OfficeRequestQuery query)
    {
        if (query.ResourceId.HasValue) Range(query.ResourceId.Value, 1, int.MaxValue, "资源编号");
        if (query.RequestId.HasValue) Range(query.RequestId.Value, 1, int.MaxValue, "申请编号");
        if (query.ApplicantUserId.HasValue) Range(query.ApplicantUserId.Value, 1, int.MaxValue, "申请人编号");
        if (query.EmployeeId.HasValue) Range(query.EmployeeId.Value, 1, int.MaxValue, "人员编号");
        if (query.From.HasValue && query.To.HasValue && (query.From >= query.To || query.To - query.From > TimeSpan.FromDays(366)))
            throw new ServiceValidationException("查询结束时间须晚于开始时间，且跨度不能超过一年。");
    }
}
