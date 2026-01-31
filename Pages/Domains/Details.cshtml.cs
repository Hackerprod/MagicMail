using MagicMail.Data;
using MagicMail.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MagicMail.Pages.Domains
{
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly CloudflareService _cloudflare;
        private readonly DnsVerificationService _dnsVerifier;
        private readonly MagicMail.Settings.AdminSettings _adminSettings;

        public DetailsModel(
            AppDbContext context, 
            CloudflareService cloudflare, 
            DnsVerificationService dnsVerifier,
            Microsoft.Extensions.Options.IOptions<MagicMail.Settings.AdminSettings> adminSettings)
        {
            _context = context;
            _cloudflare = cloudflare;
            _dnsVerifier = dnsVerifier;
            _adminSettings = adminSettings.Value;
        }

        public Domain Domain { get; set; } = default!;
        public DnsVerificationResult? DnsResult { get; set; }

        // DNS Records to display/copy
        public string DkimRecordName { get; set; } = "default._domainkey";
        public string DkimRecordValue { get; set; } = string.Empty;
        public string SpfRecordName { get; set; } = "@";
        public string SpfRecordValue { get; set; } = string.Empty; // Calculated based on current IP if possible? or standard
        public string DmarcRecordName { get; set; } = "_dmarc";
        public string DmarcRecordValue { get; set; } = "v=DMARC1; p=none";
        public string MxRecordValue { get; set; } = "";
        public string MailARecordValue { get; set; } = "";

        [TempData]
        public string? Message { get; set; }
        [TempData]
        public string? ErrorMessage { get; set; }
        [TempData]
        public string? Success { get; set; } // Added for new TempData messages
        [TempData]
        public string? Warning { get; set; } // Added for new TempData messages
        [TempData]
        public string? Error { get; set; } // Added for new TempData messages


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            Domain = domain;
            await PrepareDnsInfo();
            await LoadAliasesAsync(id);

            return Page();
        }

        private async Task PrepareDnsInfo() // Changed to async Task
        {
            // Extract public key cleanly for DNS
            var step1 = Domain.DkimPublicKey.Replace("-----BEGIN PUBLIC KEY-----", "").Replace("-----END PUBLIC KEY-----", "");
            var cleanKey = step1.Replace("\r", "").Replace("\n", "").Trim();

            DkimRecordValue = $"v=DKIM1; k=rsa; p={cleanKey}";
            
            // SPF & MX: Detect Public IP Properly
            string serverIp = "YOUR_SERVER_IP"; 
            try 
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                serverIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch
            {
                // Fallback
            }

            SpfRecordValue = $"v=spf1 mx ip4:{serverIp} -all";
            
            // MX Strategy: 
            // 1. A Record: mail.domain.com -> IP
            // 2. MX Record: domain.com -> mail.domain.com (Priority 10)
            MailARecordValue = serverIp;
            MxRecordValue = $"mail.{Domain.DomainName}";
        }

        public async Task<IActionResult> OnPostVerifyDnsAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            Domain = domain;
            
            // Get server IP
            string serverIp = "127.0.0.1";
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                serverIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { }

            // Verify DNS records
            var result = await _dnsVerifier.VerifyDomainAsync(domain, serverIp);

            if (result.AllValid)
            {
                domain.IsVerified = true;
                await _context.SaveChangesAsync();
                TempData["Success"] = "✅ All DNS records verified successfully! Domain is now active.";
            }
            else
            {
                var issues = string.Join("<br>", result.Issues.Select(i => $"• {i}"));
                TempData["Warning"] = $"DNS verification found issues:<br>{issues}";
                
                // Still mark as verified if at least 3 out of 4 are valid (partial)
                if (new[] { result.SpfValid, result.DkimValid, result.DmarcValid, result.MxValid }.Count(x => x) >= 3)
                {
                    domain.IsVerified = true;
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostSyncCloudflareAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            Domain = domain;

            // First, verify if DNS records already exist
            string serverIp = "127.0.0.1";
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                serverIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { }

            var verifyResult = await _dnsVerifier.VerifyDomainAsync(domain, serverIp);

            // If all records already exist and are valid, just mark as verified
            if (verifyResult.AllValid)
            {
                domain.IsVerified = true;
                await _context.SaveChangesAsync();
                TempData["Success"] = "✅ DNS records already configured correctly! Domain marked as verified.";
                return RedirectToPage(new { id });
            }

            // Otherwise, proceed with Cloudflare sync
            if (string.IsNullOrEmpty(domain.CloudflareZoneId))
            {
                var zId = await _cloudflare.GetZoneIdAsync(domain.DomainName);
                if (string.IsNullOrEmpty(zId))
                {
                    TempData["Error"] = "Could not find Zone ID in Cloudflare. Ensure domain exists in your CF account.";
                    return RedirectToPage(new { id });
                }
                domain.CloudflareZoneId = zId;
                await _context.SaveChangesAsync();
            }

            var step1 = domain.DkimPublicKey
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "");
            var cleanKey = step1.Replace("\r", "").Replace("\n", "").Trim();
            var dkimVal = $"v=DKIM1; k=rsa; p={cleanKey}";
            var spfVal = $"v=spf1 mx ip4:{serverIp} -all";
            var mxVal = $"mail.{domain.DomainName}";

            // Only create records that don't exist or are invalid
            bool s1 = verifyResult.SpfValid || await _cloudflare.CreateDnsRecordAsync(domain.CloudflareZoneId, "TXT", "@", spfVal);
            bool s2 = verifyResult.DkimValid || await _cloudflare.CreateDnsRecordAsync(domain.CloudflareZoneId, "TXT", $"{domain.DkimSelector}._domainkey", dkimVal);
            bool s3 = verifyResult.DmarcValid || await _cloudflare.CreateDnsRecordAsync(domain.CloudflareZoneId, "TXT", "_dmarc", "v=DMARC1; p=none");
            bool s4 = await _cloudflare.CreateDnsRecordAsync(domain.CloudflareZoneId, "A", "mail", serverIp);
            bool s5 = verifyResult.MxValid || await _cloudflare.CreateDnsRecordAsync(domain.CloudflareZoneId, "MX", "@", mxVal, 10);

            if (s1 && s2 && s3 && s4 && s5)
            {
                domain.IsVerified = true;
                await _context.SaveChangesAsync();
                TempData["Success"] = "DNS records synced to Cloudflare successfully!";
            }
            else
            {
                TempData["Warning"] = "Some records may have failed (duplicates?). Check Cloudflare dashboard.";
                domain.IsVerified = true;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            // Cleanup Cloudflare
            if (!string.IsNullOrEmpty(domain.CloudflareZoneId))
            {
                await _cloudflare.DeleteDnsRecordsForDomainAsync(domain.CloudflareZoneId);
            }

            _context.Domains.Remove(domain);
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }

        public async Task<IActionResult> OnPostTestEmailAsync(int id, string testEmail)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            try
            {
                string htmlBody = $@"
                <div style='font-family: Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 8px;'>
                    <h2 style='color: #4f46e5; text-align: center;'>MagicMail Delivery Success (via API)</h2>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                    <p style='font-size: 16px; color: #333;'>Hello,</p>
                    <p style='font-size: 16px; color: #333;'>This is a verification email sent from <strong>{domain.DomainName}</strong> using the MagicMail REST API.</p>
                    <div style='background-color: #f8fafc; padding: 15px; border-radius: 6px; margin: 20px 0;'>
                        <p style='margin: 0; color: #555; font-size: 14px;'><strong>Status:</strong> Delivered via API Queue</p>
                        <p style='margin: 5px 0 0; color: #555; font-size: 14px;'><strong>Timestamp:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
                    </div>
                    <p style='font-size: 16px; color: #333;'>If you see this message, the API Client, Authentication, and Queue Worker are all functioning correctly.</p>
                    <p style='font-size: 14px; color: #777; margin-top: 30px; text-align: center;'>Powered by MagicMail - Your Self-Hosted Email Engine</p>
                </div>";

                // Use the MagicMailClient to send the email via the local API
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var apiKey = _adminSettings.ApiKeys.FirstOrDefault() ?? "invalid-key";
                var client = new MagicMail.Client.MagicMailClient(baseUrl, apiKey);

                var result = await client.SendEmailAsync(
                    to: testEmail,
                    subject: $"[MagicMail API] Test for {domain.DomainName}",
                    body: htmlBody,
                    fromEmail: $"test@{domain.DomainName}",
                    fromName: "MagicMail API Test"
                );

                if (result.Success)
                {
                    Message = $"Test email queued successfully via API! ID: {result.MessageId}";
                }
                else
                {
                    ErrorMessage = $"API Error: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error sending email: {ex.Message}";
            }

            return RedirectToPage(new { id });
        }

        // Email Aliases
        public List<EmailAlias> Aliases { get; set; } = new();

        private async Task LoadAliasesAsync(int domainId)
        {
            Aliases = await _context.EmailAliases
                .Where(a => a.DomainId == domainId)
                .OrderBy(a => a.LocalPart)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddAliasAsync(int id, string localPart, string forwardTo)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            if (string.IsNullOrWhiteSpace(localPart) || string.IsNullOrWhiteSpace(forwardTo))
            {
                TempData["Error"] = "Both alias and forward address are required.";
                return RedirectToPage(new { id });
            }

            // Normalize
            localPart = localPart.ToLowerInvariant().Trim();
            forwardTo = forwardTo.ToLowerInvariant().Trim();

            // Check duplicate
            var exists = await _context.EmailAliases.AnyAsync(a => 
                a.DomainId == id && a.LocalPart == localPart);
            
            if (exists)
            {
                TempData["Error"] = $"Alias '{localPart}@{domain.DomainName}' already exists.";
                return RedirectToPage(new { id });
            }

            var alias = new EmailAlias
            {
                DomainId = id,
                LocalPart = localPart,
                ForwardTo = forwardTo,
                IsActive = true
            };

            _context.EmailAliases.Add(alias);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Alias '{localPart}@{domain.DomainName}' → '{forwardTo}' created.";
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostDeleteAliasAsync(int id, int aliasId)
        {
            var alias = await _context.EmailAliases.FindAsync(aliasId);
            if (alias == null || alias.DomainId != id)
            {
                TempData["Error"] = "Alias not found.";
                return RedirectToPage(new { id });
            }

            _context.EmailAliases.Remove(alias);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Alias deleted.";
            return RedirectToPage(new { id });
        }
    }
}
