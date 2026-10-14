using Microsoft.AspNetCore.Authorization;

namespace DistributedCrm.Api.Authentication;

// Marker for "the signed-in Google user's email is on GoogleAuth:AllowedEmails".
// The logic lives in AllowedEmailHandler, so the list can come from DI-injected options.
public sealed class AllowedEmailRequirement : IAuthorizationRequirement;