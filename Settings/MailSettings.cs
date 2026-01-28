namespace MagicMail.Settings
{
    public class MailSettings
    {
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public bool EnableSsl { get; set; } = true;

        public string DefaultFromEmail { get; set; } = "noreply@example.com";
        public string DefaultFromName { get; set; } = "MagicMail System";

        // DKIM Settings
        public string DkimDomain { get; set; } = string.Empty;
        public string DkimSelector { get; set; } = "default";
        public string DkimPrivateKeyPath { get; set; } = "dkim_private.pem";
    }
}
