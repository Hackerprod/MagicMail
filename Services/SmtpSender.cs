using MagicMail.Data;
using MagicMail.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MagicMail.Services
{
    public class SmtpSender
    {
        private readonly MailSettings _settings;
        private readonly DkimSigner _dkimSigner;
        private readonly ILogger<SmtpSender> _logger;

        private readonly MxResolver _mxResolver;

        public SmtpSender(IOptions<MailSettings> settings, DkimSigner dkimSigner, ILogger<SmtpSender> logger, MxResolver mxResolver)
        {
            _settings = settings.Value;
            _dkimSigner = dkimSigner;
            _logger = logger;
            _mxResolver = mxResolver;
        }

        public async Task SendAsync(EmailMessage email)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(email.FromName, email.FromEmail));
            message.To.Add(MailboxAddress.Parse(email.To));
            message.Subject = email.Subject;

            var builder = new BodyBuilder
            {
                HtmlBody = email.Body
            };
            message.Body = builder.ToMessageBody();

            // Sign with DKIM
            try 
            {
                _dkimSigner.Sign(message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sign email with DKIM. Sending unsigned.");
            }

            // Direct Delivery Logic (MTA)
            var toDomain = email.To.Split('@').Last();
            var domainName = email.FromEmail.Split('@').Last(); // For HELO
            var mxRecords = await _mxResolver.GetMxRecordsAsync(toDomain);

            if (!mxRecords.Any()) throw new Exception($"No MX records found for domain {toDomain}");

            Exception? lastException = null;

            foreach (var mxHost in mxRecords)
            {
                try
                {
                    _logger.LogInformation($"Attempting delivery to {mxHost} (25)...");
                    
                    // Force IPv4 Resolution
                    // Gmail blocked IPv6 because PTR might be missing for it.
                    // We resolve the MX hostname to an IPv4 address manually.
                    var ipAddresses = await System.Net.Dns.GetHostAddressesAsync(mxHost);
                    var ipv4Endpoint = ipAddresses.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                    if (ipv4Endpoint == null)
                    {
                        _logger.LogWarning($"No IPv4 address found for {mxHost}. Skipping.");
                        continue;
                    }

                    _logger.LogInformation($"Resolved {mxHost} to {ipv4Endpoint}");

                    using var client = new SmtpClient();

                    // CRITICAL for Spam Avoidance: Set HELO/EHLO hostname BEFORE connecting.
                    // If we have a fixed HeloHostname (rDNS), use top priority. Otherwise use sender domain.
                    client.LocalDomain = !string.IsNullOrEmpty(_settings.HeloHostname) 
                        ? _settings.HeloHostname 
                        : domainName;

                    // Also force Message-Id to use our domain to avoid leaking internal hostname
                    message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(domainName);
                    
                    // We connect by IP, so valid certificates for the domain will fail name validation against the IP.
                    // For opportunistic TLS on Port 25, we accept the certificate to ensure encryption is used even if validation fails.
                    client.CheckCertificateRevocation = false;
                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                    
                    // We must use Port 25 for inter-server communication
                    // Connect to the specific IPv4 Address
                    await client.ConnectAsync(ipv4Endpoint.ToString(), 25, SecureSocketOptions.Auto);

                    // Note: We do NOT authenticate because we are delivering TO them, we are not their user.
                    // We are acting as a server.
                                        
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                    
                    _logger.LogInformation($"Email sent successfully to {mxHost} ({ipv4Endpoint})!");
                    return; // Success
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to send to {mxHost}: {ex.Message}");
                    lastException = ex;
                }
            }

            throw lastException ?? new Exception($"Failed to deliver email to any MX server for {toDomain}");
        }
        public async Task SendTestAsync(string to, string subject, string body, string fromEmail, string fromName)
        {
            var dummyMessage = new EmailMessage
            {
                To = to,
                Subject = subject,
                Body = body,
                FromEmail = fromEmail,
                FromName = fromName
            };

            await SendAsync(dummyMessage);
        }
    }
}
