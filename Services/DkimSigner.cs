using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MagicMail.Data;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using MimeKit.Cryptography;

namespace MagicMail.Services
{
    public class DkimSigner
    {
        private readonly IServiceProvider _serviceProvider;

        public DkimSigner(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Sign(MimeMessage message)
        {
            // Necesitamos un scope para acceder a la DB (Scoped) desde Single/Scoped service logic
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Determinar dominio del sender
            if (message.From.Count == 0) return;
            var senderParams = message.From[0] as MailboxAddress;
            if (senderParams == null) return;

            var domainName = senderParams.Address.Split('@').Last().ToLower();
            
            // Buscar dominio en DB
            var domainConfig = db.Domains.FirstOrDefault(d => d.DomainName.ToLower() == domainName);
            
            if (domainConfig == null)
            {
                // Fallback o log? Por ahora simplemente no firmamos si no es un dominio gestionado.
                return;
            }

            var headers = new HeaderId[] { 
                HeaderId.From, 
                HeaderId.Subject, 
                HeaderId.To, 
                HeaderId.Date, 
                HeaderId.MessageId 
            };

            // Crear signer usando la clave privada de la DB
            // MimeKit es estricto con el formato PEM.
            // Si el usuario guardó la clave sin headers o todo en una línea, hay que arreglarlo.
            string privateKeyPem;
            try
            {
                privateKeyPem = NormalizePrivateKey(domainConfig.DkimPrivateKey);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid DKIM private key for domain '{domainConfig.DomainName}'", ex);
            }

            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(privateKeyPem));

            var signer = new MimeKit.Cryptography.DkimSigner(
                stream,
                domainConfig.DomainName,
                domainConfig.DkimSelector,
                DkimSignatureAlgorithm.RsaSha256
            )
            {
                HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
                BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
                AgentOrUserIdentifier = $"@{domainConfig.DomainName}"
            };

            message.Prepare(EncodingConstraint.SevenBit);
            signer.Sign(message, headers);
        }

        private static string NormalizePrivateKey(string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
                throw new ArgumentException("The DKIM private key is empty.", nameof(rawKey));

            var trimmed = rawKey.Trim();

            if (trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(trimmed);
                return ExportRsaPrivateKeyPem(rsa);
            }

            // Assume plain base64 (PKCS#1) with no headers.
            var keyBytes = Convert.FromBase64String(trimmed);
            return new string(PemEncoding.Write("RSA PRIVATE KEY", keyBytes));
        }

        private static string ExportRsaPrivateKeyPem(RSA rsa)
        {
            var keyBytes = rsa.ExportRSAPrivateKey();
            return new string(PemEncoding.Write("RSA PRIVATE KEY", keyBytes));
        }
    }
}
