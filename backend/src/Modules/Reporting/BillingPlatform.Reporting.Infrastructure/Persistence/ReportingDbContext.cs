using Microsoft.EntityFrameworkCore;

namespace BillingPlatform.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reporting");
    }
}
