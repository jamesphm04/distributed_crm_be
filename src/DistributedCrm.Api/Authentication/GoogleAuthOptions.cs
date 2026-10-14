using System.ComponentModel.DataAnnotations;

namespace DistributedCrm.Api.Authentication;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    // The OAuth client ID. Google ID tokens carry it as their `aud` claim,
    // so a token minted for any other app is rejected.
    [Required]
    public string ClientId { get; init; } = string.Empty;

    // The whole authorization model: a verified Google email on this list gets in, anyone else gets 403.
    [Required, MinLength(1)]
    public string[] AllowedEmails { get; init; } = [];
}