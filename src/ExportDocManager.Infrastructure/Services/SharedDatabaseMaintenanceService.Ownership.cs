using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed partial class SharedDatabaseMaintenanceService
    {
        public async Task<SharedDatabaseOwnershipSummary> GetOwnershipSummaryAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var users = await context.Users.AsNoTracking().OrderBy(user => user.Username).ToListAsync(cancellationToken).ConfigureAwait(false);
            var invoiceGroups = await LoadOwnerCountsAsync(context.Invoices.AsNoTracking(), cancellationToken).ConfigureAwait(false);
            var paymentGroups = await LoadOwnerCountsAsync(context.Payments.AsNoTracking(), cancellationToken).ConfigureAwait(false);
            var otherBusinessGroups = CombineOwnerCounts(
                await LoadOwnerCountsAsync(context.Customers.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.Exporters.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.CrmCustomers.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.CrmFollowUps.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.SupplierCompanies.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.SalesOpportunities.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.EmailTemplates.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.UserReportTemplates.AsNoTracking(), cancellationToken).ConfigureAwait(false),
                await LoadOwnerCountsAsync(context.ContainerProjects.AsNoTracking(), cancellationToken).ConfigureAwait(false));

            int invoiceTotal = invoiceGroups.Sum(group => group.Count);
            int paymentTotal = paymentGroups.Sum(group => group.Count);
            int otherBusinessTotal = otherBusinessGroups.TotalCount;
            int unassignedInvoices = invoiceGroups.FirstOrDefault(group => group.OwnerUserId == null)?.Count ?? 0;
            int unassignedPayments = paymentGroups.FirstOrDefault(group => group.OwnerUserId == null)?.Count ?? 0;
            int unassignedOtherBusiness = otherBusinessGroups.UnassignedCount;

            var ownerItems = users
                .Select(user => new SharedDatabaseOwnerSummaryItem(
                    user.Id,
                    user.Username ?? string.Empty,
                    user.FullName ?? string.Empty,
                    user.Role ?? string.Empty,
                    user.DepartmentId ?? string.Empty,
                    user.CompanyScope ?? string.Empty,
                    user.IsActive,
                    invoiceGroups.FirstOrDefault(group => group.OwnerUserId == user.Id)?.Count ?? 0,
                    paymentGroups.FirstOrDefault(group => group.OwnerUserId == user.Id)?.Count ?? 0,
                    otherBusinessGroups.GetCount(user.Id)))
                .ToArray();

            return new SharedDatabaseOwnershipSummary(
                invoiceTotal,
                unassignedInvoices,
                paymentTotal,
                unassignedPayments,
                otherBusinessTotal,
                unassignedOtherBusiness,
                ownerItems,
                OwnershipStoragePolicy);
        }

        public async Task<SharedDatabaseOwnershipTransferResult> TransferOwnershipAsync(
            SharedDatabaseOwnershipTransferRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!request.IncludeInvoices && !request.IncludePayments && !request.IncludeOtherBusinessData)
            {
                throw new InvalidOperationException("请至少选择一种需要改派的业务数据。");
            }

            if (request.ToUserId <= 0)
            {
                throw new InvalidOperationException("请选择新的归属用户。");
            }

            if (!request.OnlyUnassigned && request.FromUserId is <= 0)
            {
                throw new InvalidOperationException("来源用户无效。");
            }

            if (!request.OnlyUnassigned && request.FromUserId == request.ToUserId)
            {
                throw new InvalidOperationException("来源用户和目标用户不能相同。");
            }

            return await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, token) =>
                {
                    var targetUser = await context.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(user => user.Id == request.ToUserId && user.IsActive, token)
                        .ConfigureAwait(false)
                        ?? throw new InvalidOperationException("新的归属用户不存在或已停用。");

                    int updatedInvoices = 0;
                    int updatedPayments = 0;
                    int updatedOtherBusinessData = 0;
                    string departmentId = NormalizeOwnershipScope(
                        request.DepartmentId,
                        targetUser.DepartmentId,
                        "部门范围");
                    string companyScope = NormalizeOwnershipScope(
                        request.CompanyScope,
                        targetUser.CompanyScope,
                        "公司范围");

                    if (request.IncludeInvoices)
                    {
                        updatedInvoices = await TransferOwnedRowsAsync(
                            context,
                            context.Invoices,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                    }

                    if (request.IncludePayments)
                    {
                        updatedPayments = await TransferOwnedRowsAsync(
                            context,
                            context.Payments,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                    }

                    if (request.IncludeOtherBusinessData)
                    {
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.Customers,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.Exporters,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.CrmCustomers,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.CrmFollowUps,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.SupplierCompanies,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.SalesOpportunities,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.EmailTemplates,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.UserReportTemplates,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                        updatedOtherBusinessData += await TransferOwnedRowsAsync(
                            context,
                            context.ContainerProjects,
                            request,
                            (item, ownerUserId, department, company) =>
                            {
                                item.OwnerUserId = ownerUserId;
                                item.DepartmentId = department;
                                item.CompanyScope = company;
                            },
                            targetUser.Id,
                            departmentId,
                            companyScope,
                            token).ConfigureAwait(false);
                    }

                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return new SharedDatabaseOwnershipTransferResult(
                        true,
                        $"归属改派完成：发票 {updatedInvoices} 条，付款报销 {updatedPayments} 条，其他业务资料 {updatedOtherBusinessData} 条。",
                        updatedInvoices,
                        updatedPayments,
                        updatedOtherBusinessData,
                        OwnershipStoragePolicy);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static string NormalizeOwnershipScope(
            string requestedValue,
            string fallbackValue,
            string fieldName)
        {
            string normalized = string.IsNullOrWhiteSpace(requestedValue)
                ? (fallbackValue ?? string.Empty).Trim()
                : requestedValue.Trim();
            if (normalized.Length > 50)
            {
                throw new InvalidOperationException($"{fieldName}不能超过 50 个字符。");
            }

            return normalized;
        }

        private static async Task<List<OwnerCount>> LoadOwnerCountsAsync<TEntity>(
            IQueryable<TEntity> source,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            return await source
                .GroupBy(item => EF.Property<int?>(item, nameof(Invoice.OwnerUserId)))
                .Select(group => new OwnerCount(group.Key, group.Count()))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        private static OwnerCountAggregate CombineOwnerCounts(params IReadOnlyList<OwnerCount>[] groups)
        {
            var counts = new Dictionary<int, int>();
            int unassignedCount = 0;
            foreach (var group in groups.SelectMany(items => items))
            {
                if (group.OwnerUserId.HasValue)
                {
                    int ownerUserId = group.OwnerUserId.Value;
                    counts[ownerUserId] = counts.GetValueOrDefault(ownerUserId) + group.Count;
                }
                else
                {
                    unassignedCount += group.Count;
                }
            }

            return new OwnerCountAggregate(counts, unassignedCount);
        }

        private static async Task<int> TransferOwnedRowsAsync<TEntity>(
            AppDbContext context,
            DbSet<TEntity> source,
            SharedDatabaseOwnershipTransferRequest request,
            Action<TEntity, int, string, string> applyOwner,
            int ownerUserId,
            string departmentId,
            string companyScope,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            const int batchSize = 500;
            int updatedCount = 0;
            int lastId = 0;
            while (true)
            {
                IQueryable<TEntity> query = source.Where(item => EF.Property<int>(item, "Id") > lastId);
                if (request.OnlyUnassigned)
                {
                    query = query.Where(item => EF.Property<int?>(item, nameof(Invoice.OwnerUserId)) == null);
                }
                else if (request.FromUserId.HasValue)
                {
                    int fromUserId = request.FromUserId.Value;
                    query = query.Where(item => EF.Property<int?>(item, nameof(Invoice.OwnerUserId)) == fromUserId);
                }

                var rows = await query
                    .OrderBy(item => EF.Property<int>(item, "Id"))
                    .Take(batchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (rows.Count == 0)
                {
                    break;
                }

                foreach (var row in rows)
                {
                    applyOwner(row, ownerUserId, departmentId, companyScope);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                lastId = context.Entry(rows[^1]).Property<int>("Id").CurrentValue;
                updatedCount += rows.Count;
                context.ChangeTracker.Clear();
            }

            return updatedCount;
        }

        private sealed record OwnerCount(int? OwnerUserId, int Count);

        private sealed record OwnerCountAggregate(IReadOnlyDictionary<int, int> Counts, int UnassignedCount)
        {
            public int TotalCount => UnassignedCount + Counts.Values.Sum();

            public int GetCount(int ownerUserId) => Counts.GetValueOrDefault(ownerUserId);
        }

    }
}
