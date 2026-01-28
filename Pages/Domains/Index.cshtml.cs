using MagicMail.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MagicMail.Pages.Domains
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly MagicMail.Services.CloudflareService _cloudflare;

        public IndexModel(AppDbContext context, MagicMail.Services.CloudflareService cloudflare)
        {
            _context = context;
            _cloudflare = cloudflare;
        }

        public IList<Domain> Domains { get; set; } = default!;

        public async Task OnGetAsync()
        {
            Domains = await _context.Domains.ToListAsync();
        }

    }
}
