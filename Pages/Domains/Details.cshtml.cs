using MagicMail.Data;
using MagicMail.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MagicMail.Pages.Domains
{
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly CloudflareService _cloudflare;
        private readonly MagicMail.Settings.AdminSettings _adminSettings;

        public DetailsModel(AppDbContext context, CloudflareService cloudflare, Microsoft.Extensions.Options.IOptions<MagicMail.Settings.AdminSettings> adminSettings)
        {
            _context = context;
            _cloudflare = cloudflare;
            _adminSettings = adminSettings.Value;
        }

        public Domain Domain { get; set; } = default!;

        [TempData]
        public string? Message { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var domain = await _context.Domains.FindAsync(id);
            if (domain == null) return NotFound();

            Domain = domain;
            return Page();
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
                // This mimics how external apps will use the system
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
                    Message = $"API Error: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                Message = $"Error sending email: {ex.Message}";
            }

            return RedirectToPage(new { id });
        }
    }
}
