using DistributedCrm.Application.Contracts;
using DistributedCrm.Domain.Entities;

namespace DistributedCrm.Application.Mapping;

public static class ReturnRequestMapper
{
    public static ReturnRequestResponse ToResponse(this ReturnRequest returnRequest)
    {
        return new ReturnRequestResponse(
            returnRequest.Id,
            returnRequest.OrderReference,
            returnRequest.CustomerEmail,
            returnRequest.Item,
            returnRequest.Reason,
            returnRequest.Status.ToString(),
            returnRequest.CreatedAt,
            returnRequest.UpdatedAt);
    }
}