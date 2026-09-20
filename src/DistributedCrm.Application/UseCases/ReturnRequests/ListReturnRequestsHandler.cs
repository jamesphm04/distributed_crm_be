using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Mapping;

namespace DistributedCrm.Application.UseCases.ReturnRequests;

public class ListReturnRequestsHandler
{
    private readonly IReturnRequestRepository _repository;

    public ListReturnRequestsHandler(IReturnRequestRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<ReturnRequestResponse>> HandleAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var (items, totalCount) = await _repository.ListAsync(page, pageSize, ct);

        var responses = items.Select(r => r.ToResponse()).ToList();

        return new PagedResult<ReturnRequestResponse>(responses, totalCount, page, pageSize);
    }
}
