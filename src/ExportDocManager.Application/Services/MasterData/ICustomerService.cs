using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface ICustomerService
    {
        Task<int> SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default);
        Task<List<Customer>> GetAllCustomersAsync(CancellationToken cancellationToken = default);
        Task<Customer> GetCustomerByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> DeleteCustomerAsync(int id, CancellationToken cancellationToken = default);
        Task<Customer> GetCustomerByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<List<Customer>> SearchCustomersAsync(string keyword, CancellationToken cancellationToken = default);
    }
}
