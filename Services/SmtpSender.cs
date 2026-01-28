using MagicMail.Data;
using MagicMail.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text.RegularExpressions;

namespace MagicMail.Services
{
    public class SmtpSender
    {
        private readonly MailSettings _settings;
        private readonly DkimSigner _dkimSigner;
        private readonly ILogger<SmtpSender> _logger;
        private readonly MxResolver _mxResolver;
        private readonly ServerIdentityResolver _identityResolver;

        public SmtpSender(
            IOptions<MailSettings> settings,
            DkimSigner dkimSigner,
            ILogger<SmtpSender> logger,
            MxResolver mxResolver,
            ServerIdentityResolver identityResolver)
        {
            _settings = settings.Value;
            _dkimSigner = dkimSigner;
            _logger = logger;
            _mxResolver = mxResolver;
            _identityResolver = identityResolver;
        }

        public async Task SendAsync(EmailMessage email)
        {
            if (string.IsNullOrWhiteSpace(email.To))
                throw new ArgumentException("Recipient address is required.", nameof(email));

            var fromEmail = !string.IsNullOrWhiteSpace(email.FromEmail)
                ? email.FromEmail
                : _settings.DefaultFromEmail;

            var fromName = !string.IsNullOrWhiteSpace(email.FromName)
                ? email.FromName
                : _settings.DefaultFromName;

            var domainName = fromEmail.Split('@').Last();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(MailboxAddress.Parse(email.To));
            message.Subject = email.Subject ?? string.Empty;

            var htmlBody = email.Body ?? string.Empty;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = BuildPlainText(htmlBody)
            };
            message.Body = builder.ToMessageBody();

            // Ensure stable headers before DKIM signing
            if (string.IsNullOrWhiteSpace(message.MessageId))
            {
                message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(domainName);
            }

            if (message.Date == DateTimeOffset.MinValue)
            {
                message.Date = DateTimeOffset.UtcNow;
            }

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
            var heloHostname = !string.IsNullOrEmpty(_settings.HeloHostname)
                ? _settings.HeloHostname
                : await _identityResolver.GetPtrHostnameAsync() ?? domainName;
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
                    client.LocalDomain = heloHostname;

                    // Also force Message-Id to use our domain to avoid leaking internal hostname
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

        private static string BuildPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            // Remove script/style content which should not appear in plaintext.
            var withoutCode = Regex.Replace(html, @"<(script|style)[^>]*?>.*?</\1>", string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Insert new lines for common block-level tags.
            var withBreaks = Regex.Replace(withoutCode, @"<(br|/p|/div|/li|/tr)\s*/?>", "\n",
                RegexOptions.IgnoreCase);
            withBreaks = Regex.Replace(withBreaks, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);

            // Strip remaining tags.
            var noTags = Regex.Replace(withBreaks, "<[^>]+>", " ", RegexOptions.Singleline);

            // Decode HTML entities and clean whitespace.
            var decoded = System.Net.WebUtility.HtmlDecode(noTags);
            decoded = Regex.Replace(decoded, @"[ \t]+\n", "\n");
            decoded = Regex.Replace(decoded, @"\n{3,}", "\n\n");
            decoded = Regex.Replace(decoded, @"[ \t]{2,}", " ");

            return decoded.Trim();
        }
    }
}
