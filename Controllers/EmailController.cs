using MagicMail.Data;
using MagicMail.Models;
using MagicMail.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MagicMail.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [MagicMail.Authentication.ApiKeyAuthAttribute]
    public class EmailController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly MailSettings _mailSettings;

        public EmailController(AppDbContext context, IOptions<MailSettings> mailSettings)
        {
            _context = context;
            _mailSettings = mailSettings.Value;
        }

        [HttpPost("send")]
        public async Task<IActionResult> EnqueueEmail([FromBody] EmailRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // 1. Validate From Domain
            var fromEmail = request.FromEmail ?? _mailSettings.DefaultFromEmail;
            var fromDomain = fromEmail.Split('@').Last();
            
            // Check if we manage this domain (and it is verified?)
            // Ideally we only allow verified domains.
            var domainExists = _context.Domains.Any(d => d.DomainName == fromDomain);
            if (!domainExists)
            {
                return StatusCode(403, new { Error = $"Sending from domain '{fromDomain}' is not allowed. Domain not registered." });
            }

            var emailMessage = new EmailMessage
            {
                To = request.To,
                Subject = request.Subject,
                Body = request.Body,
                FromEmail = fromEmail,
                FromName = request.FromName ?? _mailSettings.DefaultFromName,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                Attempts = 0
            };

            _context.EmailMessages.Add(emailMessage);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Email queued successfully", Id = emailMessage.Id });
        }
        
        [HttpGet("status/{id}")]
        public async Task<IActionResult> GetStatus(int id)
        {
            var email = await _context.EmailMessages.FindAsync(id);
            if (email == null) return NotFound();
            
            return Ok(new { 
                email.Id, 
                email.Status, 
                email.SentAt, 
                email.Attempts, 
                email.LastError 
            });
        }
    }
}
