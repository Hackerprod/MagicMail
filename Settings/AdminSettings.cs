namespace MagicMail.Settings
{
    public class AdminSettings
    {
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "password"; // Default, should be changed
        public string CloudflareApiKey { get; set; } = string.Empty;
        public List<string> ApiKeys { get; set; } = new List<string>();
    }
}
