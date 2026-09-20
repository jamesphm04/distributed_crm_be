using DistributedCrm.Domain.Entities;

namespace DistributedCrm.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

public class DistributedCrmDbContext : DbContext
{
    public DistributedCrmDbContext(DbContextOptions<DistributedCrmDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DistributedCrmDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}