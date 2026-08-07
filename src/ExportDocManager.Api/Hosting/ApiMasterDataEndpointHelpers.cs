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
            return await repository.GetByIdAsync(id, cancellationToken);
        }

        private static async Task<Port> FindPortByIdAsync(
            IPortReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            return await repository.GetByIdAsync(id, cancellationToken);
        }

        private static async Task<Unit> FindUnitByIdAsync(
            IUnitReadRepository repository,
            int id,
            CancellationToken cancellationToken)
        {
            return await repository.GetByIdAsync(id, cancellationToken);
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
