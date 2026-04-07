using DotLearn.Payment.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotLearn.Payment.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Payment.Models.Entities.Payment> Payments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment.Models.Entities.Payment>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.Currency).IsRequired().HasMaxLength(10);
            entity.Property(p => p.Provider).IsRequired().HasMaxLength(50);
            entity.Property(p => p.TransactionId).IsRequired().HasMaxLength(200);
            entity.Property(p => p.OrderId).IsRequired().HasMaxLength(200);
            entity.HasIndex(p => p.TransactionId).IsUnique(); // idempotency
        });
    }
}
