using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static async Task<Payee> FindPayeeByIdAsync(
            IPayeeReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            var rows = await repository.QueryAsync(new PayeeReadQuery(), cancellationToken);
            return rows.FirstOrDefault(row => row.Id == id);
        }

        private static async Task<Port> FindPortByIdAsync(
            IPortReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            var rows = await repository.QueryAsync(new PortReadQuery(), cancellationToken);
            return rows.FirstOrDefault(row => row.Id == id);
        }

        private static async Task<Unit> FindUnitByIdAsync(
            IUnitReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            var rows = await repository.QueryAsync(new UnitReadQuery(), cancellationToken);
            return rows.FirstOrDefault(row => row.Id == id);
        }

        private static async Task<HsCode> FindHsCodeByIdAsync(
            IHsCodeReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            return await repository.GetByIdAsync(id, cancellationToken);
        }
    }
}
