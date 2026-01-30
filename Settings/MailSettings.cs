namespace MagicMail.Settings
{
    public class MailSettings
    {
        // Default sender info (fallback if not specified per email)
        public string DefaultFromEmail { get; set; } = "noreply@example.com";
        public string DefaultFromName { get; set; } = "MagicMail System";

        // Advanced Delivery
        public string? HeloHostname { get; set; } // Opcional: Para forzar el nombre en HELO/EHLO (ej: vmi...contaboserver.net)

        // Inbound SMTP (Email Forwarding)
        public bool EnableInboundSmtp { get; set; } = false;
        public int InboundPort { get; set; } = 25;
        public int MaxInboundMessageSize { get; set; } = 10 * 1024 * 1024; // 10 MB
    }
}
