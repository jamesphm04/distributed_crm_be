using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DistributedCrm.Api.Authentication;

public sealed class AllowedEmailHandler : AuthorizationHandler<AllowedEmailRequirement>
{
    private readonly HashSet<string> _allowedEmails;
    private readonly ILogger<AllowedEmailHandler> _logger;

    public AllowedEmailHandler(IOptions<GoogleAuthOptions> options, ILogger<AllowedEmailHandler> logger)
    {
        // Email addresses are case-insensitive in practice, so "James@..." in config must match "james@..." in the token.
        _allowedEmails = new HashSet<string>(options.Value.AllowedEmails, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowedEmailRequirement requirement)
    {
        // RequireAuthenticatedUser() already rejects anonymous requests (missing, expired or invalid token);
        // without this, every one of them would also be logged as a missing email.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        // Raw Google claim names - Program.cs turns off the default renaming to long schema URIs.
        var email = context.User.FindFirst("email")?.Value;
        var emailVerified = context.User.FindFirst("email_verified")?.Value;

        if (email is null || !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Denied: token has no verified email");
            return Task.CompletedTask;
        }

        if (!_allowedEmails.Contains(email))
        {
            _logger.LogWarning("Denied: {Email} is not on GoogleAuth:AllowedEmails", email);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}