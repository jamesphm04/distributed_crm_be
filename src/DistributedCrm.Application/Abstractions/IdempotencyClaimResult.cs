using DistributedCrm.Application.Contracts;

namespace DistributedCrm.Application.Abstractions;

public record IdempotencyClaimResult(IdempotencyClaimStatus Status, ReturnRequestResponse? ExistingResponse);