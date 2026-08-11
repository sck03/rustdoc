using System.Threading.Tasks;
using System.Threading;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public interface IPaymentService
    {
        Task<int> SavePaymentAsync(Payment payment, CancellationToken cancellationToken = default);
        Task<bool> DeletePaymentAsync(int id, CancellationToken cancellationToken = default);
    }
}
