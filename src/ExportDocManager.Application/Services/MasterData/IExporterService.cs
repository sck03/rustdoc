using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.MasterData
{
    public interface IExporterService
    {
        Task<int> SaveExporterAsync(Exporter exporter);
        Task<List<Exporter>> GetAllExportersAsync();
        Task<Exporter> GetExporterByIdAsync(int id);
        Task<bool> DeleteExporterAsync(int id);
        Task<Exporter> GetExporterByNameAsync(string name);
        Task<List<Exporter>> SearchExportersAsync(string keyword);
    }
}
