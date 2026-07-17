using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class LocalSingleWindowCollaborationDataSource : ISingleWindowCollaborationDataSource
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ISingleWindowWorkstationRegistryService _workstationRegistryService;
        private readonly BusinessDataAccessScope _businessDataAccessScope;

        public LocalSingleWindowCollaborationDataSource(
            IDbContextFactory<AppDbContext> contextFactory,
            ISingleWindowWorkstationRegistryService workstationRegistryService,
            DatabaseConnectionSettings databaseSettings,
            BusinessDataAccessScope businessDataAccessScope = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _workstationRegistryService = workstationRegistryService ?? throw new ArgumentNullException(nameof(workstationRegistryService));
            var normalizedSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
            _businessDataAccessScope = businessDataAccessScope ?? new BusinessDataAccessScope(normalizedSettings);
        }

        public async Task<SingleWindowCollaborationPageResult> QueryPageAsync(
            SingleWindowCollaborationPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var normalizedQuery = NormalizePageQuery(query);
            using var context = await CreateContextWithCurrentWorkstationAsync(cancellationToken);
            var tickets = BuildTicketQuery(
                context,
                normalizedQuery.BusinessType,
                normalizedQuery.Status,
                normalizedQuery.Keyword);
            tickets = _businessDataAccessScope.ApplyOperationTicketScope(tickets, context);
            var totalTicketCount = await tickets.CountAsync(cancellationToken);
            var pagedTickets = await ProjectTicketRows(ApplyTicketOrdering(tickets))
                .Skip((normalizedQuery.PageNumber - 1) * normalizedQuery.PageSize)
                .Take(normalizedQuery.PageSize)
                .ToListAsync(cancellationToken);
            var workstations = await ProjectWorkstationRows(
                    BuildWorkstationQuery(context, normalizedQuery.IncludeDisabledWorkstations))
                .ToListAsync(cancellationToken);

            return new SingleWindowCollaborationPageResult
            {
                Tickets = pagedTickets,
                Workstations = workstations,
                TotalTicketCount = totalTicketCount,
                PageNumber = normalizedQuery.PageNumber,
                PageSize = normalizedQuery.PageSize
            };
        }

        public Task<IReadOnlyList<SingleWindowOperationTicketRow>> QueryTicketsAsync(
            SingleWindowCollaborationQuery query,
            CancellationToken cancellationToken = default)
        {
            return QueryTicketsCoreAsync(query, cancellationToken);
        }

        public Task<IReadOnlyList<SingleWindowWorkstationRow>> QueryWorkstationsAsync(
            CancellationToken cancellationToken = default)
        {
            return QueryWorkstationsCoreAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<SingleWindowOperationTicketRow>> QueryTicketsCoreAsync(
            SingleWindowCollaborationQuery query,
            CancellationToken cancellationToken)
        {
            var normalizedQuery = NormalizeTicketQuery(query);
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var tickets = BuildTicketQuery(
                context,
                normalizedQuery.BusinessType,
                normalizedQuery.Status,
                normalizedQuery.Keyword);
            tickets = _businessDataAccessScope.ApplyOperationTicketScope(tickets, context);
            return await ProjectTicketRows(
                    ApplyTicketOrdering(
                        tickets))
                .Take(normalizedQuery.Take)
                .ToListAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<SingleWindowWorkstationRow>> QueryWorkstationsCoreAsync(CancellationToken cancellationToken)
        {
            using var context = await CreateContextWithCurrentWorkstationAsync(cancellationToken);
            return await ProjectWorkstationRows(
                    BuildWorkstationQuery(context, includeDisabledWorkstations: true))
                .ToListAsync(cancellationToken);
        }

        private async Task<AppDbContext> CreateContextWithCurrentWorkstationAsync(CancellationToken cancellationToken)
        {
            await _workstationRegistryService.EnsureCurrentWorkstationAsync(cancellationToken);
            return await _contextFactory.CreateDbContextAsync(cancellationToken);
        }

        private static SingleWindowCollaborationPageQuery NormalizePageQuery(SingleWindowCollaborationPageQuery query)
        {
            return new SingleWindowCollaborationPageQuery
            {
                BusinessType = TextSearchHelper.NormalizeFilter(query?.BusinessType),
                Status = SingleWindowCollaborationStatusCatalog.Normalize(query?.Status),
                Keyword = TextSearchHelper.NormalizeFilter(query?.Keyword),
                PageNumber = Math.Max(1, query?.PageNumber ?? 1),
                PageSize = Math.Clamp(query?.PageSize ?? 50, 1, 500),
                IncludeDisabledWorkstations = query?.IncludeDisabledWorkstations ?? false
            };
        }

        private static SingleWindowCollaborationQuery NormalizeTicketQuery(SingleWindowCollaborationQuery query)
        {
            return new SingleWindowCollaborationQuery
            {
                BusinessType = TextSearchHelper.NormalizeFilter(query?.BusinessType),
                Status = SingleWindowCollaborationStatusCatalog.Normalize(query?.Status),
                Keyword = TextSearchHelper.NormalizeFilter(query?.Keyword),
                Take = Math.Clamp(query?.Take ?? 200, 1, 500)
            };
        }

        private static IQueryable<SwOperationTicket> BuildTicketQuery(
            AppDbContext context,
            string businessType,
            string status,
            string keyword)
        {
            var normalizedKeyword = TextSearchHelper.NormalizeFilter(keyword);
            var tickets = context.SwOperationTickets.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(businessType))
            {
                tickets = tickets.Where(ticket => ticket.BusinessType == businessType);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                tickets = tickets.Where(ticket => ticket.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                tickets = ApplyKeywordFilter(tickets, normalizedKeyword);
            }

            return tickets;
        }

        private static IQueryable<SwOperationTicket> ApplyTicketOrdering(IQueryable<SwOperationTicket> tickets)
        {
            return tickets
                .OrderByDescending(ticket => ticket.RequestedAt)
                .ThenByDescending(ticket => ticket.Id);
        }

        private static IQueryable<SwOperatorWorkstation> BuildWorkstationQuery(
            AppDbContext context,
            bool includeDisabledWorkstations)
        {
            var workstations = context.SwOperatorWorkstations.AsNoTracking().AsQueryable();
            if (!includeDisabledWorkstations)
            {
                workstations = workstations.Where(item => item.IsEnabled);
            }

            return workstations
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.MachineName);
        }

        private static IQueryable<SingleWindowOperationTicketRow> ProjectTicketRows(IQueryable<SwOperationTicket> tickets)
        {
            return tickets.Select(ticket => new SingleWindowOperationTicketRow
            {
                TicketId = ticket.Id,
                BusinessType = ticket.BusinessType,
                SourceInvoiceId = ticket.SourceInvoiceId,
                DocumentId = ticket.DocumentId,
                BatchId = ticket.BatchId,
                Status = ticket.Status,
                RequestedBy = ticket.RequestedBy,
                AssignedOperator = ticket.AssignedOperator,
                AssignedWorkstationId = ticket.AssignedWorkstationId,
                Priority = ticket.Priority,
                RequestedAt = ticket.RequestedAt,
                AssignedAt = ticket.AssignedAt,
                SubmittedAt = ticket.SubmittedAt,
                CompletedAt = ticket.CompletedAt,
                LastError = ticket.LastError
            });
        }

        private static IQueryable<SingleWindowWorkstationRow> ProjectWorkstationRows(IQueryable<SwOperatorWorkstation> workstations)
        {
            return workstations.Select(item => new SingleWindowWorkstationRow
            {
                WorkstationId = item.Id,
                MachineName = item.MachineName,
                ProfileId = item.ProfileId,
                OperatorName = item.OperatorName,
                CanSubmitAgentConsignment = item.CanSubmitAgentConsignment,
                CanSubmitCustomsCoo = item.CanSubmitCustomsCoo,
                IsEnabled = item.IsEnabled,
                Remarks = item.Remarks,
                UpdatedAt = item.UpdatedAt
            });
        }

        private static IQueryable<SwOperationTicket> ApplyKeywordFilter(
            IQueryable<SwOperationTicket> tickets,
            string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return tickets;
            }

            var stringMatches = tickets.ApplyKeywordSearch(
                keyword,
                ticket => ticket.BusinessType,
                ticket => ticket.Status,
                ticket => ticket.RequestedBy,
                ticket => ticket.AssignedOperator,
                ticket => ticket.LastError);

            if (!int.TryParse(keyword, out var keywordNumber))
            {
                return stringMatches;
            }

            return stringMatches.Union(
                tickets.Where(ticket =>
                    ticket.SourceInvoiceId == keywordNumber ||
                    ticket.DocumentId == keywordNumber ||
                    (ticket.BatchId.HasValue && ticket.BatchId.Value == keywordNumber)));
        }
    }
}
