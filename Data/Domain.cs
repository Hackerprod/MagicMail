using System.ComponentModel.DataAnnotations;

namespace MagicMail.Data
{
    public class Domain
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string DomainName { get; set; } = string.Empty;

        public string? CloudflareZoneId { get; set; }

        // DKIM Configuration
        public string DkimSelector { get; set; } = "default";
        
        [Required]
        public string DkimPrivateKey { get; set; } = string.Empty; // PEM format
        
        [Required]
        public string DkimPublicKey { get; set; } = string.Empty; // PEM format for reference

        public bool IsVerified { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
