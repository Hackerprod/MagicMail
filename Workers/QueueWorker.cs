using MagicMail.Data;
using MagicMail.Services;
using Microsoft.EntityFrameworkCore;

namespace MagicMail.Workers
{
    public class QueueWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<QueueWorker> _logger;
        private const int MaxAttempts = 5;

        public QueueWorker(IServiceProvider serviceProvider, ILogger<QueueWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("QueueWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Ignore gracefully
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email queue.");
                }

                if (stoppingToken.IsCancellationRequested) break;
                
                try 
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<SmtpSender>();

            // Buscar correos pendientes y que ya les toque reintento
            var pendingEmails = await db.EmailMessages
                .Where(e => (e.Status == "Pending" || e.Status == "Retrying") 
                         && (e.NextAttemptAfter == null || e.NextAttemptAfter <= DateTime.UtcNow))
                .OrderBy(e => e.CreatedAt)
                .Take(10) // Procesar lotes de 10
                .ToListAsync(stoppingToken);

            foreach (var email in pendingEmails)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    _logger.LogInformation($"Sending email {email.Id} to {email.To}...");
                    
                    await sender.SendAsync(email);

                    email.Status = "Sent";
                    email.SentAt = DateTime.UtcNow;
                    email.LastError = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send email {email.Id}.");
                    
                    email.Attempts++;
                    email.LastError = ex.Message;

                    if (email.Attempts >= MaxAttempts)
                    {
                        email.Status = "Failed";
                        email.NextAttemptAfter = null;
                    }
                    else
                    {
                        email.Status = "Retrying";
                        // Exponential backoff: 30s, 1m, 2m, 4m, 8m
                        email.NextAttemptAfter = DateTime.UtcNow.AddSeconds(30 * Math.Pow(2, email.Attempts - 1));
                    }
                }
            }

            if (pendingEmails.Any())
            {
                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
