using MagicMail.Data;
using MagicMail.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MagicMail.Pages.Domains
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly CloudflareService _cloudflare;
        private readonly DnsVerificationService _dnsVerifier;

        public IndexModel(AppDbContext context, CloudflareService cloudflare, DnsVerificationService dnsVerifier)
        {
            _context = context;
            _cloudflare = cloudflare;
            _dnsVerifier = dnsVerifier;
        }

        public IList<Domain> Domains { get; set; } = default!;

        public async Task OnGetAsync()
        {
            Domains = await _context.Domains.ToListAsync();

            // Get server IP for verification
            string serverIp = "127.0.0.1";
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                serverIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { }

            // Silently verify DNS for each domain and auto-update status
            bool anyUpdated = false;
            foreach (var domain in Domains)
            {
                if (!domain.IsVerified)
                {
                    var result = await _dnsVerifier.VerifyDomainAsync(domain, serverIp);
                    if (result.AllValid)
                    {
                        domain.IsVerified = true;
                        anyUpdated = true;
                    }
                }
            }

            if (anyUpdated)
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
