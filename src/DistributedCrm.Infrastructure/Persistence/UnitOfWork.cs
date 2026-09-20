using DistributedCrm.Application.Abstractions;

namespace DistributedCrm.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly DistributedCrmDbContext _dbContext;

    public UnitOfWork(DistributedCrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _dbContext.SaveChangesAsync(ct);
    }
}