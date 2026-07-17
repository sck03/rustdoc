using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Security
{
    public interface ICurrentUserContext
    {
        User CurrentUser { get; }
    }
}
