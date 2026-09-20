namespace DistributedCrm.Application.Exceptions;

public class IdempotencyConflictException(string idempotencyKey)
    : Exception($"A request with idempotency key '{idempotencyKey}' is already in progress.");