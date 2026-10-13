using MerchantManagement.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<SubMerchant> SubMerchants => Set<SubMerchant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(254).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).HasMaxLength(20).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
        });

        b.Entity<Merchant>(e =>
        {
            e.Property(m => m.Name).HasMaxLength(200).IsRequired();
            e.Property(m => m.LegalName).HasMaxLength(200).IsRequired();
            e.Property(m => m.Email).HasMaxLength(254).IsRequired();
            e.Property(m => m.CountryCode).HasMaxLength(2).IsRequired();
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(m => m.Email).IsUnique();
            e.HasIndex(m => m.Name);
            e.HasIndex(m => m.Status);
            e.HasIndex(m => m.CreatedAt);
        });

        b.Entity<SubMerchant>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200).IsRequired();
            e.Property(s => s.ExternalReference).HasMaxLength(64).IsRequired();
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(s => s.Merchant).WithMany(m => m.SubMerchants)
                .HasForeignKey(s => s.MerchantId).OnDelete(DeleteBehavior.Cascade);
            // One external reference per parent; also serves lookups by (MerchantId, ...).
            e.HasIndex(s => new { s.MerchantId, s.ExternalReference }).IsUnique();
        });
    }
}
