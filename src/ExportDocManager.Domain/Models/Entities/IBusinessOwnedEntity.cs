namespace ExportDocManager.Models.Entities
{
    public interface IBusinessOwnedEntity
    {
        int? OwnerUserId { get; set; }
        string DepartmentId { get; set; }
        string CompanyScope { get; set; }
    }

    public interface ISharedBusinessTemplate : IBusinessOwnedEntity
    {
        string Status { get; set; }
        string ShareScope { get; set; }
    }
}
