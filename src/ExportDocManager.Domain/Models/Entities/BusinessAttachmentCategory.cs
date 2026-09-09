using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities;

public sealed class BusinessAttachmentCategory
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string CompanyScope { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string NameNormalized { get; set; } = string.Empty;
    [ConcurrencyCheck] public int VersionNumber { get; set; } = 1;
}
