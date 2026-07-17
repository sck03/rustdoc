using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class SwOperatorWorkstation
    {
        public int Id { get; set; }

        [MaxLength(80)]
        public string MachineName { get; set; } = string.Empty;

        public int? ProfileId { get; set; }

        [MaxLength(80)]
        public string OperatorName { get; set; } = string.Empty;

        public bool CanSubmitAgentConsignment { get; set; }

        public bool CanSubmitCustomsCoo { get; set; }

        public bool IsEnabled { get; set; } = true;

        public string Remarks { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
