using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IAuxiliaryService
    {
        Task<List<Port>> GetAllPortsAsync(CancellationToken cancellationToken = default);
        Task<List<Port>> SearchPortsAsync(string keyword, CancellationToken cancellationToken = default);
        Task SavePortAsync(Port port, CancellationToken cancellationToken = default);
        Task DeletePortAsync(int id, CancellationToken cancellationToken = default);
        Task<List<Unit>> GetAllUnitsAsync(CancellationToken cancellationToken = default);
        Task<List<Unit>> SearchUnitsAsync(string keyword, CancellationToken cancellationToken = default);
        Task SaveUnitAsync(Unit unit, CancellationToken cancellationToken = default);
        Task DeleteUnitAsync(int id, CancellationToken cancellationToken = default);
        Task<List<string>> GetUnitsByEnglishNameAsync(string nameEn, CancellationToken cancellationToken = default);
    }
}
