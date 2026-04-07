using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotLearn.Payment.Data;

public class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentDbContext>();
        optionsBuilder.UseSqlServer("Server=dotlearn-db.c7ge68ueyfep.ap-southeast-2.rds.amazonaws.com,1433;Database=PaymentDb;User Id=admin;Password=DotLearn2026;TrustServerCertificate=True");
        return new PaymentDbContext(optionsBuilder.Options);
    }
}
