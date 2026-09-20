using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Mapping;

namespace DistributedCrm.Application.UseCases.ReturnRequests;

public class CancelReturnRequestHandler
{
    private readonly IReturnRequestRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReturnRequestCache _cache;

    public CancelReturnRequestHandler(IReturnRequestRepository repository, IUnitOfWork unitOfWork, IReturnRequestCache cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<ReturnRequestResponse?> HandleAsync(Guid id, CancellationToken ct = default)
    {
        var returnRequest = await _repository.GetByIdAsync(id, ct);
        if (returnRequest is null)
        {
            return null;
        }

        returnRequest.Cancel();
        await _unitOfWork.SaveChangesAsync(ct);

        await _cache.InvalidateAsync(id, ct);
        
        return returnRequest.ToResponse();
    }
}
