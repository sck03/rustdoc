using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExportDocManager.Models.Entities
{
    public class SwClientProfile
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string ProfileKey { get; set; } = string.Empty;

        [MaxLength(80)]
        public string ProfileName { get; set; } = string.Empty;

        [MaxLength(64)]
        public string StationKey { get; set; } = string.Empty;

        [MaxLength(80)]
        public string MachineName { get; set; } = Environment.MachineName;

        [MaxLength(120)]
        public string CompanyScope { get; set; } = string.Empty;

        [MaxLength(120)]
        public string CardIdentifier { get; set; } = string.Empty;

        [MaxLength(512)]
        public string ProtectedHandoffSecret { get; set; } = string.Empty;

        [NotMapped]
        public string StationAssignmentCode { get; set; } = string.Empty;

        [MaxLength(520)]
        public string CustomsCooClientRootPath { get; set; } = string.Empty;

        [MaxLength(520)]
        public string AgentConsignmentClientRootPath { get; set; } = string.Empty;

        public bool CanSubmitCustomsCoo { get; set; } = true;

        public bool CanSubmitAgentConsignment { get; set; } = true;

        public bool IsEnabled { get; set; } = true;

        public bool IsActive { get; set; }

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
