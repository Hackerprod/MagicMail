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
            
            // ServerIp can be inferred or configured globally, asking it every time is user hostile if it's the same VPS.
            // But for SPF we need it. Let's make it optional or default to current.
            // For now, let's keep it but make it optional? Or better, remove it and use the one detected/configured.
            // User rules said "user requested to remove placeholders".
            // Let's remove it from Input and calculate it later in Details.
            // public string ServerIp { get; set; } = string.Empty;
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

                // 2. Cloudflare Integration (REMOVED: Now manual in Details page)
                // We just default IsVerified to false until they set up DNS.
                var zoneId = ""; // Optional, will be fetched if they sync manually later? Or we can try to fetch it now but not fail?
                
                // Let's try to fetch ZoneID silently, but NOT create records.
                try 
                {
                    zoneId = await _cloudflare.GetZoneIdAsync(Input.DomainName) ?? "";
                }
                catch 
                {
                    // Ignore, user can sync later.
                }

                bool dnsSuccess = false; // By default not verified until they add records.

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
