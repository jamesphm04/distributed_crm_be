namespace DistributedCrm.Application.Exceptions;

// Thrown when a key can't be claimed because the store is down. Creating the request anyway would let a retry
// with the same key create a duplicate, so the request is refused and the client retries later with the same key.
public class IdempotencyStoreUnavailableException(Exception innerException)
    : Exception("The idempotency store is unavailable. Retry the request with the same Idempotency-Key.", innerException);
