using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IAuxiliaryService
    {
        Task<List<Port>> GetAllPortsAsync();
        Task<List<Port>> SearchPortsAsync(string keyword);
        Task SavePortAsync(Port port);
        Task DeletePortAsync(int id);
        Task<List<Unit>> GetAllUnitsAsync();
        Task<List<Unit>> SearchUnitsAsync(string keyword);
        Task SaveUnitAsync(Unit unit);
        Task DeleteUnitAsync(int id);
        Task<List<string>> GetUnitsByEnglishNameAsync(string nameEn);
    }
}
