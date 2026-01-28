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
            // Asumimos que la Key en DB es PEM string.
            // DkimSigner de MimeKit suele recibir Stream o path.
            // Crearemos un stream en memoria.
            
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(domainConfig.DkimPrivateKey));

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
    }
}
