using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Core
{
    public interface IPaymentService
    {
        Task<int> SavePaymentAsync(Payment payment);
        Task<bool> DeletePaymentAsync(int id);
    }
}
