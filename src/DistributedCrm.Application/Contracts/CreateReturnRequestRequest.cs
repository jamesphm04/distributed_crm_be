using System.ComponentModel.DataAnnotations;

namespace DistributedCrm.Application.Contracts;

public record CreateReturnRequestRequest(
    [Required][MaxLength(100)] string OrderReference,
    [Required][EmailAddress][MaxLength(320)] string CustomerEmail,
    [Required][MaxLength(200)] string Item,
    [Required][MaxLength(1000)] string Reason);
