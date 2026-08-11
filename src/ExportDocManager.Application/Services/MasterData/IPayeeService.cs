using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IPayeeService
    {
        Task<int> SavePayeeAsync(Payee payee, CancellationToken cancellationToken = default);
        Task<List<Payee>> GetAllPayeesAsync(CancellationToken cancellationToken = default);
        Task<bool> DeletePayeeAsync(int id, CancellationToken cancellationToken = default);
        Task<List<Payee>> SearchPayeesAsync(string keyword, CancellationToken cancellationToken = default);
    }
}
