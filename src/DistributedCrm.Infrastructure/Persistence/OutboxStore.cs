using DistributedCrm.Application.Abstractions;

namespace DistributedCrm.Infrastructure.Persistence;

public class OutboxStore : IOutboxStore
{
    private readonly DistributedCrmDbContext _dbContext;

    public OutboxStore(DistributedCrmDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueReturnRequestCreatedAsync(Guid returnRequestId, CancellationToken ct = default)
    {
        var message = OutboxMessage.ForReturnRequestCreated(returnRequestId);
        await _dbContext.OutboxMessages.AddAsync(message, ct);
    }
}
