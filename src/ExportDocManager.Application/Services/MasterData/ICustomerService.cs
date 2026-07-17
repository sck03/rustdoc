using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface ICustomerService
    {
        Task<int> SaveCustomerAsync(Customer customer);
        Task<List<Customer>> GetAllCustomersAsync();
        Task<Customer> GetCustomerByIdAsync(int id);
        Task<bool> DeleteCustomerAsync(int id);
        Task<Customer> GetCustomerByNameAsync(string name);
        Task<List<Customer>> SearchCustomersAsync(string keyword);
    }
}
