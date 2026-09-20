using DistributedCrm.Domain.Entities;

namespace DistributedCrm.Application.Abstractions;

public interface IReturnRequestRepository
{
    Task AddAsync(ReturnRequest returnRequest, CancellationToken ct = default);

    Task<ReturnRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ReturnRequest> Items, int TotalCount)> ListAsync(
        int page, int pageSize, CancellationToken ct = default);
}