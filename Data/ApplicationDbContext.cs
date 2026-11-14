using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Models;

namespace HaliYikamaAPI.Data;

/// <summary>
/// Uygulama veritabanı bağlam sınıfı.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>Siparişler tablosu</summary>
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.CustomerName)
                .HasMaxLength(100);

            entity.Property(e => e.CustomerPhone)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.CustomerAddress)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.ServiceType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.ReceiptNumber)
                .HasMaxLength(50);

            entity.Property(e => e.TotalPrice)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.UpdatedAt);

            // Indexes
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CustomerPhone);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
