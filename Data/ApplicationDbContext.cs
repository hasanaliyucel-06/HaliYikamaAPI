using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Models;

namespace HaliYikamaAPI.Data;

/// <summary>
/// Uygulama veritabanı bağlam sınıfı.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public static readonly Guid DefaultCompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DefaultDriverId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>İşletmeler tablosu</summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>Sürücüler tablosu</summary>
    public DbSet<Driver> Drivers => Set<Driver>();

    /// <summary>Siparişler tablosu</summary>
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Company
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();

            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Address).HasMaxLength(250);
            entity.Property(e => e.CodePrefix).IsRequired().HasMaxLength(10);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasMany(e => e.Drivers)
                .WithOne(d => d.Company)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Orders)
                .WithOne(o => o.Company)
                .HasForeignKey(o => o.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Default seed company (Giriş: deniz@haliyikama.com / Deniz123!)
            entity.HasData(new Company
            {
                Id = DefaultCompanyId,
                Name = "Deniz Halı Yıkama",
                Email = "deniz@haliyikama.com",
                PasswordHash = "eJicADF1ds7n6+bbv++gGQ==:7i8IXu4ChIrJCigG6YVrS7y4odRcMN1MYIc3qTooPHs=",
                Phone = "05321234567",
                Address = "Merkez Mah. Sanayi Cad. No:10",
                CodePrefix = "DNZ",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        // Driver
        modelBuilder.Entity<Driver>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AccessCode).IsUnique();
            entity.HasIndex(e => e.CompanyId);

            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AccessCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.VehiclePlate).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasMany(e => e.Orders)
                .WithOne(o => o.Driver)
                .HasForeignKey(o => o.DriverId)
                .OnDelete(DeleteBehavior.SetNull);

            // Default seed driver (Hızlı kod: DNZ-101)
            entity.HasData(new Driver
            {
                Id = DefaultDriverId,
                CompanyId = DefaultCompanyId,
                FullName = "Ahmet Usta (Örnek Sürücü)",
                Phone = "05551112233",
                AccessCode = "DNZ-101",
                VehiclePlate = "34 ABC 123",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        // Order
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.CompanyId)
                .IsRequired();

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
            entity.HasIndex(e => e.CompanyId);
            entity.HasIndex(e => e.DriverId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CustomerPhone);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
