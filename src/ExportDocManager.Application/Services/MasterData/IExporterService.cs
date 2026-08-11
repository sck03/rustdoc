using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IExporterService
    {
        Task<int> SaveExporterAsync(Exporter exporter, CancellationToken cancellationToken = default);
        Task<List<Exporter>> GetAllExportersAsync(CancellationToken cancellationToken = default);
        Task<Exporter?> GetExporterByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> DeleteExporterAsync(int id, CancellationToken cancellationToken = default);
        Task<Exporter?> GetExporterByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<List<Exporter>> SearchExportersAsync(string keyword, CancellationToken cancellationToken = default);
    }
}
