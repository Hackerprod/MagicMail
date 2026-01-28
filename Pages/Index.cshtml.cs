using MagicMail.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MagicMail.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;

        public IndexModel(AppDbContext context)
        {
            _context = context;
        }

        public int TotalSent { get; set; }
        public int ActiveDomains { get; set; }
        public string ReputationScore { get; set; } = "100%";
        public string ReputationLabel { get; set; } = "Excellent";

        public async Task OnGetAsync()
        {
            // 1. Total emails with Status "Sent"
            TotalSent = await _context.EmailMessages.CountAsync(m => m.Status == "Sent");

            // 2. Verified Domains
            ActiveDomains = await _context.Domains.CountAsync(d => d.IsVerified);

            // 3. Simple Reputation (Sent / (Sent + Failed))
            var failedCount = await _context.EmailMessages.CountAsync(m => m.Status == "Failed");
            var totalAttempts = TotalSent + failedCount;

            if (totalAttempts > 0)
            {
                double score = (double)TotalSent / totalAttempts * 100;
                ReputationScore = $"{score:F1}%";

                if (score >= 98) ReputationLabel = "Excellent";
                else if (score >= 90) ReputationLabel = "Good";
                else if (score >= 70) ReputationLabel = "Fair";
                else ReputationLabel = "Poor";
            }
            else
            {
                ReputationScore = "100%"; // Start optimistic
            }
        }
    }
}
