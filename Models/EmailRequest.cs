using System.ComponentModel.DataAnnotations;

namespace MagicMail.Models
{
    public class EmailRequest
    {
        [Required]
        [EmailAddress]
        public string To { get; set; } = string.Empty;

        [Required]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        // Opcionales, si no se envían se usarán los defaults del sistema
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
    }
}
