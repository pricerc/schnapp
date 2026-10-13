using System.Security.Claims;
using Medo;
using Schnapp.Contracts;
using Schnapp.Domain;

namespace Schnapp.Api.Auth;

/// <summary>
/// Strongly typed view of the authenticated user taken from <see cref="ClaimsPrincipal"/> claims.
/// </summary>
/// <remarks>
/// Populated by <see cref="BearerTokenMiddleware"/> after a valid API token is resolved.
/// Endpoint handlers call <see cref="FromPrincipal"/> instead of reading claim strings ad hoc.
/// </remarks>
public sealed class AuthUser
{
    /// <summary>Schnapp user id (<see cref="ClaimTypes.NameIdentifier"/>).</summary>
    public Uuid7 Id { get; init; }

    /// <summary>Login email (<see cref="ClaimTypes.Email"/>).</summary>
    public string Email { get; init; } = "";

    /// <summary>Role string, typically <see cref="UserRole.User"/> or <see cref="UserRole.Admin"/>.</summary>
    public string Role { get; init; } = UserRole.User;

    /// <summary>Id of the <c>api_token</c> row that authenticated this request.</summary>
    public Uuid7 TokenId { get; init; }

    /// <summary>Whether <see cref="Role"/> is <see cref="UserRole.Admin"/>.</summary>
    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>
    /// Builds an <see cref="AuthUser"/> from claims set by <see cref="BearerTokenMiddleware"/>.
    /// </summary>
    public static AuthUser FromPrincipal(ClaimsPrincipal principal)
    {
        var id = Uuid7Text.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Missing user id claim."));
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? "";
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? UserRole.User;
        var tokenId = Uuid7Text.Parse(principal.FindFirstValue("token_id")
            ?? throw new InvalidOperationException("Missing token_id claim."));
        return new AuthUser { Id = id, Email = email, Role = role, TokenId = tokenId };
    }
}
