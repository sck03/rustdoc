using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwClientProfile
    {
        public int Id { get; set; }

        [MaxLength(80)]
        public string ProfileName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string MachineName { get; set; } = Environment.MachineName;

        [MaxLength(520)]
        public string ImportRootPath { get; set; } = string.Empty;

        [MaxLength(520)]
        public string ReceiptRootPath { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string BusinessDirectoryOverridesJson { get; set; } = string.Empty;

        public bool CanSubmitCustomsCoo { get; set; } = true;

        public bool CanSubmitAgentConsignment { get; set; } = true;

        public bool IsEnabled { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
