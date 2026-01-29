# MagicMail

**MagicMail** is a self-hosted, lightweight **transactional email sending platform** built with ASP.NET Core. It allows you to send authenticated emails directly from your own server without relying on third-party email services like SendGrid or Mailgun.

## ✨ Key Features

- **Direct SMTP Delivery**: Sends emails directly to recipients' mail servers (MX lookup) without needing a relay SMTP.
- **DKIM Signing**: Automatically signs all outgoing emails with DKIM for improved deliverability.
- **SPF/DMARC Ready**: Generates the correct DNS records for SPF, DKIM, DMARC, and MX.
- **Cloudflare Integration**: One-click DNS record synchronization to Cloudflare. No manual DNS editing required.
- **Multi-Provider Support**: Displays all required DNS records for manual configuration in GoDaddy, Namecheap, or any other DNS provider.
- **Queue-Based Processing**: Emails are queued in a local SQLite database and processed by a background worker with retry logic.
- **API Key Authentication**: Secure REST API for sending emails programmatically.
- **Admin Dashboard**: Modern Tailwind CSS interface for managing domains and monitoring email status.

---

## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        MagicMail Server                      │
├──────────────────────────────────────────────────────────────┤
│  API Layer          │  /api/email/send (POST)                │
│  (REST + API Key)   │  /api/email/status/{id} (GET)          │
├─────────────────────┼────────────────────────────────────────┤
│  Processing         │  QueueWorker (Background Service)      │
│                     │  └─> SmtpSender (Direct MX Delivery)   │
│                     │      └─> DkimSigner (RSA-SHA256)       │
├─────────────────────┼────────────────────────────────────────┤
│  DNS Management     │  CloudflareService (API v4)            │
│                     │  └─> Auto-sync: SPF, DKIM, DMARC, MX   │
├─────────────────────┼────────────────────────────────────────┤
│  Data Layer         │  SQLite (Domains, EmailMessages)       │
└──────────────────────────────────────────────────────────────┘
```

---

## 🚀 Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- A server with **Port 25 open** for outbound SMTP
- A domain with DNS access (Cloudflare recommended for automation)

### Installation

```bash
# Clone the repository
git clone https://github.com/Hackerprod/MagicMail.git
cd MagicMail

# Copy and configure settings
cp appsettings.example.json appsettings.json
# Edit appsettings.json with your configuration

# Restore dependencies and run
dotnet restore
dotnet run
```

The server will start on `http://localhost:1122`.

---

## ⚙️ Configuration

Edit `appsettings.json`:

```json
{
  "MailSettings": {
    "DefaultFromEmail": "noreply@yourdomain.com",
    "DefaultFromName": "Your App",
    "HeloHostname": "mail.yourdomain.com"  // Optional: Override EHLO hostname (must match rDNS)
  },
  "AdminSettings": {
    "Username": "admin",
    "Password": "YourSecurePassword",
    "CloudflareApiKey": "YOUR_GLOBAL_API_KEY_OR_TOKEN",
    "ApiKeys": [
      "your-api-key-for-sending-emails"
    ]
  }
}
```

### HeloHostname (Important for VPS)

If your server's reverse DNS (PTR record) is set to a hostname like `vps123.provider.net`, you should set `HeloHostname` to match it. This prevents rDNS mismatch errors that can hurt deliverability.

---

## 🌐 DNS Setup

### Option A: Automatic (Cloudflare)

1. Add your **Cloudflare Global API Key** to `appsettings.json`.
2. Go to **Domains** in the dashboard and add your domain.
3. Click **"Sync to Cloudflare"**.

MagicMail will automatically create:
- `TXT` record for **SPF**
- `TXT` record for **DKIM** (at `default._domainkey`)
- `TXT` record for **DMARC** (at `_dmarc`)
- `A` record for `mail.yourdomain.com`
- `MX` record pointing to `mail.yourdomain.com`

### Option B: Manual (GoDaddy, Namecheap, etc.)

The dashboard displays all required DNS records in a table format. Simply copy each record and add it to your DNS provider:

| Type | Name | Value |
|------|------|-------|
| `A` | `mail` | `YOUR_SERVER_IP` |
| `MX` | `@` | `mail.yourdomain.com` (Priority: 10) |
| `TXT` | `@` | `v=spf1 mx ip4:YOUR_SERVER_IP -all` |
| `TXT` | `default._domainkey` | `v=DKIM1; k=rsa; p=YOUR_PUBLIC_KEY...` |
| `TXT` | `_dmarc` | `v=DMARC1; p=none` |

---

## 📡 API Usage

### Send an Email

```bash
curl -X POST http://localhost:1122/api/email/send \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-api-key" \
  -d '{
    "to": "recipient@example.com",
    "subject": "Hello from MagicMail",
    "body": "<h1>Welcome!</h1><p>This is a test email.</p>",
    "fromEmail": "hello@yourdomain.com",
    "fromName": "Your App"
  }'
```

**Response:**
```json
{
  "message": "Email queued successfully",
  "id": 42
}
```

### Check Email Status

```bash
curl http://localhost:1122/api/email/status/42 \
  -H "X-Api-Key: your-api-key"
```

**Response:**
```json
{
  "id": 42,
  "status": "Sent",
  "sentAt": "2025-01-28T12:00:00Z",
  "attempts": 1,
  "lastError": null
}
```

---

## 🔐 Security

- **API Key Authentication**: All API endpoints require a valid `X-Api-Key` header.
- **Admin Authentication**: Dashboard access requires username/password login.
- **DKIM Private Keys**: Stored encrypted in the SQLite database per domain.

---

## 📊 Deliverability Tips

1. **Set Reverse DNS (PTR)**: Contact your VPS provider to set the PTR record for your IP.
2. **Use HeloHostname**: Ensure your EHLO hostname matches your rDNS.
3. **Warm Up Your IP**: Start by sending small volumes and gradually increase.
4. **Monitor Blacklists**: Use [MXToolbox](https://mxtoolbox.com/) to check your IP reputation.
5. **Test with Mail-Tester**: Send a test email to [mail-tester.com](https://www.mail-tester.com/) and aim for 10/10.

---

## 🛠️ CLI Commands

### Generate DKIM Keys

```bash
dotnet run -- gen-dkim
```

This generates `dkim_private.pem` and `dkim_public.pem` files and prints the DNS record value.

---

## 📁 Project Structure

```
MagicMail/
├── Controllers/          # REST API endpoints
├── Services/
│   ├── CloudflareService.cs      # Cloudflare DNS integration
│   ├── DkimSigner.cs             # DKIM signing logic
│   ├── MxResolver.cs             # MX record lookup
│   ├── ServerIdentityResolver.cs # rDNS/PTR detection
│   └── SmtpSender.cs             # Direct SMTP delivery
├── Workers/
│   └── QueueWorker.cs    # Background email processor
├── Pages/                # Razor Pages (Admin UI)
├── Data/                 # EF Core models and DbContext
└── Settings/             # Configuration POCOs
```

---

## 📜 License

MIT License. See [LICENSE](LICENSE) for details.

---

## 🤝 Contributing

Contributions are welcome! Please open an issue or submit a pull request.

---

**Built with ❤️ using ASP.NET Core, MimeKit, and MailKit.**
