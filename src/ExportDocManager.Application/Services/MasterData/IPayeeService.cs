using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IPayeeService
    {
        Task<int> SavePayeeAsync(Payee payee);
        Task<List<Payee>> GetAllPayeesAsync();
        Task<bool> DeletePayeeAsync(int id);
        Task<List<Payee>> SearchPayeesAsync(string keyword);
    }
}
