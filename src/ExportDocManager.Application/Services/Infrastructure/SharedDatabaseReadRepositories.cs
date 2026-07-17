using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IInvoiceListReadRepository
    {
        Task<PagedResult<Invoice>> QueryPageAsync(
            InvoiceListPageQuery query,
            CancellationToken cancellationToken = default);
    }

    public interface IPaymentReadRepository
    {
        Task<PagedResult<Payment>> QueryPageAsync(
            PaymentPageQuery query,
            CancellationToken cancellationToken = default);
    }

    public interface IPaymentDetailReadRepository
    {
        Task<Payment> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default);
    }

    public interface IQueryReadRepository
    {
        Task<PagedResult<Invoice>> QueryPageAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Invoice>> QueryAllAsync(
            QueryPageQuery query,
            CancellationToken cancellationToken = default);
    }

    public interface IAuditLogReadRepository
    {
        Task<PagedResult<AuditLog>> QueryPageAsync(
            AuditLogPageQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AuditLog>> QueryAllAsync(
            AuditLogPageQuery query,
            int maxCount = 2000,
            CancellationToken cancellationToken = default);
    }
}
