namespace DistributedCrm.Application.Contracts;

public record CreateReturnRequestRequest(
    string OrderReference,
    string CustomerEmail,
    string Item,
    string Reason);