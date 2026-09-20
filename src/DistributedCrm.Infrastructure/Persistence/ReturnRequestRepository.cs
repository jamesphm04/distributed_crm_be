using DistributedCrm.Application.Abstractions;
using DistributedCrm.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedCrm.Infrastructure.Persistence;

public class ReturnRequestRepository : IReturnRequestRepository
{
    private readonly DistributedCrmDbContext _dbContext;

    public ReturnRequestRepository(DistributedCrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ReturnRequest returnRequest, CancellationToken ct = default)
    {
        await _dbContext.ReturnRequests.AddAsync(returnRequest, ct);
    }

    public async Task<ReturnRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.ReturnRequests
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<(IReadOnlyList<ReturnRequest> Items, int TotalCount)> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _dbContext.ReturnRequests.OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}