using MagicMail.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- MODO GENERACIÓN DE CLAVES ---
if (args.Length > 0 && args[0] == "gen-dkim")
{
    Console.WriteLine("Generando claves DKIM...");
    using var rsa = System.Security.Cryptography.RSA.Create(2048);

    // Clave Privada
    var privBytes = rsa.ExportPkcs8PrivateKey();
    var privBase64 = Convert.ToBase64String(privBytes, Base64FormattingOptions.InsertLineBreaks);
    var privPem = "-----BEGIN PRIVATE KEY-----" + Environment.NewLine + privBase64 + Environment.NewLine + "-----END PRIVATE KEY-----";
    File.WriteAllText("dkim_private.pem", privPem);
    Console.WriteLine($"[OK] Clave privada guardada en: {Path.GetFullPath("dkim_private.pem")}");

    // Clave Pública
    var pubBytes = rsa.ExportSubjectPublicKeyInfo();
    var pubBase64 = Convert.ToBase64String(pubBytes, Base64FormattingOptions.InsertLineBreaks);
    var pubPem = "-----BEGIN PUBLIC KEY-----" + Environment.NewLine + pubBase64 + Environment.NewLine + "-----END PUBLIC KEY-----";
    File.WriteAllText("dkim_public.pem", pubPem);
    Console.WriteLine($"[OK] Clave pública guardada en: {Path.GetFullPath("dkim_public.pem")}");

    // Valor DNS
    var dnsValue = Convert.ToBase64String(pubBytes);
    Console.WriteLine("\n=== COPIA ESTO A CLOUDFLARE (Registro TXT) ===");
    Console.WriteLine("Nombre: default._domainkey");
    Console.WriteLine($"Valor:  v=DKIM1; k=rsa; p={dnsValue}");
    Console.WriteLine("==============================================\n");
    return;
}
// ---------------------------------

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Domains"); // Proteger carpeta Domains
});
builder.Services.AddEndpointsApiExplorer();

// Auth
builder.Services.AddAuthentication("CookieAuth")
    .AddCookie("CookieAuth", options =>
    {
        options.Cookie.Name = "MagicMail.Auth";
        options.LoginPath = "/Account/Login";
    });

// Configurar Settings
builder.Services.Configure<MagicMail.Settings.MailSettings>(builder.Configuration.GetSection("MailSettings"));
builder.Services.Configure<MagicMail.Settings.AdminSettings>(builder.Configuration.GetSection("AdminSettings"));

// Registros de servicios Core
builder.Services.AddSingleton<MagicMail.Services.DkimSigner>();
builder.Services.AddSingleton<MagicMail.Services.MxResolver>(); // DNS Resolver
builder.Services.AddSingleton<MagicMail.Services.ServerIdentityResolver>();
builder.Services.AddScoped<MagicMail.Services.SmtpSender>();
builder.Services.AddHostedService<MagicMail.Workers.QueueWorker>();
builder.Services.AddHttpClient<MagicMail.Services.CloudflareService>(); // Cloudflare

// Configurar SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=magicmail.db"));

var app = builder.Build();

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles(); // Para CSS/JS
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages(); // UI routes

app.Run("http://*:1122");
