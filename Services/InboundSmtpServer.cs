using MagicMail.Data;
using MagicMail.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpServer;
using SmtpServer.Mail;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;

namespace MagicMail.Services
{
    /// <summary>
    /// Inbound SMTP server that receives emails and forwards them based on configured aliases.
    /// </summary>
    public class InboundSmtpServer : BackgroundService
    {
        private readonly ILogger<InboundSmtpServer> _logger;
        private readonly MailSettings _settings;
        private readonly IServiceProvider _serviceProvider;

        public InboundSmtpServer(
            ILogger<InboundSmtpServer> logger,
            IOptions<MailSettings> settings,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _settings = settings.Value;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_settings.EnableInboundSmtp)
            {
                _logger.LogInformation("Inbound SMTP is disabled. Skipping server startup.");
                return;
            }

            _logger.LogInformation("Starting Inbound SMTP Server on port {Port}...", _settings.InboundPort);

            var options = new SmtpServerOptionsBuilder()
                .ServerName(Environment.MachineName)
                .Port(_settings.InboundPort)
                .MaxMessageSize(_settings.MaxInboundMessageSize)
                .Build();

            var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
            serviceProvider.Add(new ForwardingMessageStore(_serviceProvider, _logger));
            serviceProvider.Add((IMailboxFilterFactory)new ForwardingMailboxFilter(_serviceProvider, _logger));

            var smtpServer = new SmtpServer.SmtpServer(options, serviceProvider);

            try
            {
                await smtpServer.StartAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Inbound SMTP Server stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inbound SMTP Server error.");
            }
        }
    }

    /// <summary>
    /// Validates that we only accept mail for domains we manage.
    /// </summary>
    public class ForwardingMailboxFilter : IMailboxFilter, IMailboxFilterFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public ForwardingMailboxFilter(IServiceProvider serviceProvider, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public IMailboxFilter CreateInstance(ISessionContext context) => this;

        public Task<bool> CanAcceptFromAsync(ISessionContext context, IMailbox @from, int size, CancellationToken cancellationToken)
        {
            // Accept all senders (we're a receiving server)
            return Task.FromResult(true);
        }

        public async Task<bool> CanDeliverToAsync(ISessionContext context, IMailbox to, IMailbox @from, CancellationToken cancellationToken)
        {
            var address = to.AsAddress();
            var parts = address.Split('@');
            if (parts.Length != 2) return false;

            var localPart = parts[0].ToLowerInvariant();
            var domainPart = parts[1].ToLowerInvariant();

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check if we manage this domain
            var domain = await db.Domains.FirstOrDefaultAsync(d => d.DomainName.ToLower() == domainPart, cancellationToken);
            if (domain == null)
            {
                _logger.LogWarning("Rejected mail to {Address}: Domain not managed.", address);
                return false;
            }

            // Check if alias exists (or catch-all)
            var aliasExists = await db.EmailAliases.AnyAsync(a =>
                a.DomainId == domain.Id &&
                a.IsActive &&
                (a.LocalPart.ToLower() == localPart || a.LocalPart == "*"),
                cancellationToken);

            if (!aliasExists)
            {
                _logger.LogWarning("Rejected mail to {Address}: No alias configured.", address);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Stores incoming messages by forwarding them to the configured alias destination.
    /// </summary>
    public class ForwardingMessageStore : MessageStore
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public ForwardingMessageStore(IServiceProvider serviceProvider, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public override async Task<SmtpResponse> SaveAsync(
            ISessionContext context,
            IMessageTransaction transaction,
            ReadOnlySequence<byte> buffer,
            CancellationToken cancellationToken)
        {
            try
            {
                // Parse the incoming message
                using var stream = new MemoryStream();
                foreach (var segment in buffer)
                {
                    await stream.WriteAsync(segment, cancellationToken);
                }
                stream.Position = 0;

                var message = await MimeMessage.LoadAsync(stream, cancellationToken);

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var smtpSender = scope.ServiceProvider.GetRequiredService<SmtpSender>();

                // Process each recipient
                foreach (var recipient in transaction.To)
                {
                    var address = recipient.AsAddress();
                    var parts = address.Split('@');
                    var localPart = parts[0].ToLowerInvariant();
                    var domainPart = parts[1].ToLowerInvariant();

                    var domain = await db.Domains.FirstOrDefaultAsync(d => d.DomainName.ToLower() == domainPart, cancellationToken);
                    if (domain == null) continue;

                    // Find matching alias (exact match first, then catch-all)
                    var alias = await db.EmailAliases
                        .Where(a => a.DomainId == domain.Id && a.IsActive)
                        .Where(a => a.LocalPart.ToLower() == localPart || a.LocalPart == "*")
                        .OrderByDescending(a => a.LocalPart.ToLower() == localPart) // Prefer exact match
                        .FirstOrDefaultAsync(cancellationToken);

                    if (alias == null) continue;

                    _logger.LogInformation("Forwarding email from {From} to {To} via alias {Alias}@{Domain}",
                        transaction.From?.AsAddress() ?? "unknown",
                        alias.ForwardTo,
                        alias.LocalPart,
                        domain.DomainName);

                    // Create forwarded message
                    var forwardedEmail = new EmailMessage
                    {
                        To = alias.ForwardTo,
                        Subject = message.Subject ?? "(No Subject)",
                        Body = message.HtmlBody ?? message.TextBody ?? "",
                        FromEmail = $"redirect@{domain.DomainName}",
                        FromName = $"Forwarded via {domain.DomainName}",
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow
                    };

                    // Add original headers as a note
                    var originalFrom = message.From.ToString();
                    forwardedEmail.Body = $"<div style='background:#f0f0f0;padding:10px;margin-bottom:15px;font-size:12px;'>" +
                        $"<strong>Forwarded for:</strong> {address}<br>" +
                        $"<strong>Original From:</strong> {originalFrom}" +
                        $"</div>{forwardedEmail.Body}";

                    db.EmailMessages.Add(forwardedEmail);
                }

                await db.SaveChangesAsync(cancellationToken);
                return SmtpResponse.Ok;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbound email");
                return SmtpResponse.TransactionFailed;
            }
        }
    }
}
