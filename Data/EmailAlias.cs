using System.ComponentModel.DataAnnotations;

namespace MagicMail.Data
{
    public class EmailAlias
    {
        [Key]
        public int Id { get; set; }

        public int DomainId { get; set; }
        public Domain Domain { get; set; } = null!;

        [Required]
        [MaxLength(64)]
        public string LocalPart { get; set; } = string.Empty; // "support", "info", "*" (catch-all)

        [Required]
        [EmailAddress]
        public string ForwardTo { get; set; } = string.Empty; // "real@gmail.com"

        public bool IsActive { get; set; } = true;

        public bool IncludeForwardHeader { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
