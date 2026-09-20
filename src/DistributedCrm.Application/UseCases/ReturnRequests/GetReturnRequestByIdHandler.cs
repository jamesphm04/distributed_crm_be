using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Mapping;

namespace DistributedCrm.Application.UseCases.ReturnRequests;

public class GetReturnRequestByIdHandler
{
    private readonly IReturnRequestRepository _repository;
    private readonly IReturnRequestCache _cache;

    public GetReturnRequestByIdHandler(IReturnRequestRepository repository, IReturnRequestCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ReturnRequestResponse?> HandleAsync(Guid id, CancellationToken ct = default)
    {
        // try to get from cache first
        var cached = await _cache.GetAsync(id, ct);
        if (cached is not null)
        {
            return cached;
        }
        
        // try to get from db
        var returnRequest = await _repository.GetByIdAsync(id, ct);
        
        if (returnRequest is null)
        {
            return null;
        }
    
        // save to cache
        var response = returnRequest.ToResponse();
        await _cache.SetAsync(id, response, ct);

        return response;
    }
}
