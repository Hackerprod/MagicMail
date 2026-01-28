using System;
using System.ComponentModel.DataAnnotations;

namespace MagicMail.Data
{
    public class EmailMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string To { get; set; } = string.Empty;
        
        [Required]
        public string Subject { get; set; } = string.Empty;
        
        [Required]
        public string Body { get; set; } = string.Empty;
        
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
        
        // Status: Pending, Sent, Failed, Retrying
        public string Status { get; set; } = "Pending";
        
        public int Attempts { get; set; } = 0;
        public string? LastError { get; set; }
        public DateTime? NextAttemptAfter { get; set; }
    }
}
