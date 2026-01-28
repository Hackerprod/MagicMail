using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using MagicMail.Data;
using MagicMail.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MagicMail.Pages.Domains
{
    public class CreateModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly CloudflareService _cloudflare;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(AppDbContext context, CloudflareService cloudflare, ILogger<CreateModel> logger)
        {
            _context = context;
            _cloudflare = cloudflare;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            public string DomainName { get; set; } = string.Empty;
            
            [Required]
            public string ServerIp { get; set; } = string.Empty;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            try 
            {
                // 1. Generate DKIM Keys
                using var rsa = RSA.Create(2048);
                var privKey = "-----BEGIN PRIVATE KEY-----\r\n" + 
                              Convert.ToBase64String(rsa.ExportPkcs8PrivateKey(), Base64FormattingOptions.InsertLineBreaks) + 
                              "\r\n-----END PRIVATE KEY-----";
                
                var pubKeyBytes = rsa.ExportSubjectPublicKeyInfo();
                var pubKeyClean = Convert.ToBase64String(pubKeyBytes); // One line for DNS
                var pubKeyPem = "-----BEGIN PUBLIC KEY-----\r\n" + 
                                Convert.ToBase64String(pubKeyBytes, Base64FormattingOptions.InsertLineBreaks) + 
                                "\r\n-----END PUBLIC KEY-----";

                // 2. Cloudflare Integration
                var zoneId = await _cloudflare.GetZoneIdAsync(Input.DomainName);
                
                if (string.IsNullOrEmpty(zoneId))
                {
                    ModelState.AddModelError(string.Empty, "Could not find Cloudflare Zone. Check domain name and API Key configuration.");
                    return Page();
                }

                bool dnsSuccess = true;

                // DKIM
                dnsSuccess &= await _cloudflare.CreateDnsRecordAsync(zoneId, "TXT", "default._domainkey", $"v=DKIM1; k=rsa; p={pubKeyClean}");
                
                // SPF
                dnsSuccess &= await _cloudflare.CreateDnsRecordAsync(zoneId, "TXT", "@", $"v=spf1 ip4:{Input.ServerIp} -all");
                
                // DMARC
                dnsSuccess &= await _cloudflare.CreateDnsRecordAsync(zoneId, "TXT", "_dmarc", "v=DMARC1; p=none");

                if (!dnsSuccess)
                {
                    ModelState.AddModelError(string.Empty, "Some DNS records failed to create. check logs.");
                }

                // 3. Save to DB
                var domain = new Domain
                {
                    DomainName = Input.DomainName,
                    CloudflareZoneId = zoneId,
                    DkimPrivateKey = privKey,
                    DkimPublicKey = pubKeyPem,
                    IsVerified = dnsSuccess, // Assume verified if DNS verify returns OK (simplified)
                    CreatedAt = DateTime.UtcNow
                };

                _context.Domains.Add(domain);
                await _context.SaveChangesAsync();

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating domain");
                ModelState.AddModelError(string.Empty, "An unexpected error occurred.");
                return Page();
            }
        }
    }
}
