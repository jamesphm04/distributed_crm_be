namespace DistributedCrm.Application.Abstractions;

public enum IdempotencyClaimStatus
{
    Claimed, // you're first — proceed to do the work
    InProgress, // someone else is mid-flight on this key right now
    Completed // someone already finished this key — here's their response
}

