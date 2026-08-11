using System;
using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string EntityName { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty; // Insert, Update, Delete
        
        [Required]
        public string EntityId { get; set; } = string.Empty;
        
        public string? OldValues { get; set; } // JSON
        
        public string? NewValues { get; set; } // JSON
        
        [MaxLength(100)]
        public string? UserId { get; set; } // Useful for multi-user phase
        
        public DateTime Timestamp { get; set; }
    }
}
