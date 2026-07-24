using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Infrastructure
{
    public interface ICustomerReadRepository
    {
        Task<IReadOnlyList<Customer>> QueryAsync(CustomerReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IExporterReadRepository
    {
        Task<IReadOnlyList<Exporter>> QueryAsync(ExporterReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IPayeeReadRepository
    {
        Task<IReadOnlyList<Payee>> QueryAsync(PayeeReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IProductReadRepository
    {
        Task<IReadOnlyList<Product>> QueryAsync(ProductReadQuery query, CancellationToken cancellationToken = default);

        Task<PagedResult<Product>> QueryPageAsync(ProductReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IPortReadRepository
    {
        Task<IReadOnlyList<Port>> QueryAsync(PortReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IUnitReadRepository
    {
        Task<IReadOnlyList<Unit>> QueryAsync(UnitReadQuery query, CancellationToken cancellationToken = default);
    }

    public interface IHsCodeReadRepository
    {
        Task<IReadOnlyList<HsCode>> QueryAsync(HsCodeReadQuery query, CancellationToken cancellationToken = default);

        Task<PagedResult<HsCode>> QueryPageAsync(HsCodeReadQuery query, CancellationToken cancellationToken = default);

        Task<HsCode> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

        Task<HsCode> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<int>> FindExistingIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default);
    }
}
