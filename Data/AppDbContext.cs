using Microsoft.EntityFrameworkCore;

namespace MagicMail.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<EmailMessage> EmailMessages { get; set; }
        public DbSet<Domain> Domains { get; set; }
        public DbSet<EmailAlias> EmailAliases { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Índices para optimizar la búsqueda de correos pendientes
            modelBuilder.Entity<EmailMessage>()
                .HasIndex(e => e.Status);
                
            modelBuilder.Entity<EmailMessage>()
                .HasIndex(e => e.NextAttemptAfter);

            // Índice compuesto para búsqueda rápida de aliases
            modelBuilder.Entity<EmailAlias>()
                .HasIndex(a => new { a.DomainId, a.LocalPart })
                .IsUnique();
        }
    }
}
