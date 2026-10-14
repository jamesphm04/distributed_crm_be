using DistributedCrm.Application.Abstractions;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Exceptions;
using DistributedCrm.Application.Mapping;
using DistributedCrm.Domain.Entities;

namespace DistributedCrm.Application.UseCases.ReturnRequests;

public class CreateReturnRequestHandler
{
    private readonly IReturnRequestRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutboxStore _outbox;
    private readonly IIdempotencyStore _idempotencyStore;

    public CreateReturnRequestHandler(
        IReturnRequestRepository repository,
        IUnitOfWork unitOfWork,
        IOutboxStore outbox,
        IIdempotencyStore idempotencyStore)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _idempotencyStore = idempotencyStore;
    }

    public async Task<ReturnRequestResponse> HandleAsync(
        string idempotencyKey, CreateReturnRequestRequest request, CancellationToken ct = default)
    {
        var claim = await _idempotencyStore.TryClaimAsync(idempotencyKey, ct);

        switch (claim.Status)
        {
            case IdempotencyClaimStatus.Completed:
                return claim.ExistingResponse!;
            case IdempotencyClaimStatus.InProgress:
                throw new IdempotencyConflictException(idempotencyKey);
        }
        
        var returnRequest = ReturnRequest.Create(
            Guid.CreateVersion7(),
            request.OrderReference,
            request.CustomerEmail,
            request.Item,
            request.Reason);
        
        await _repository.AddAsync(returnRequest, ct);
        await _outbox.EnqueueReturnRequestCreatedAsync(returnRequest.Id, ct);
        await _unitOfWork.SaveChangesAsync(ct); // row + outbox entry commit together, atomically

        var response = returnRequest.ToResponse();

        await _idempotencyStore.StoreResultAsync(idempotencyKey, response, ct);

        return response;
    }
}