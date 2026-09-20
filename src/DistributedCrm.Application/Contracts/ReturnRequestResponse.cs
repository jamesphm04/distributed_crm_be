namespace DistributedCrm.Application.Contracts;

public record ReturnRequestResponse(
    Guid Id,
    string OrderReference,
    string CustomerEmail,
    string Item,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);