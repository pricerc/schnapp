using System.Security.Claims;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain.Auth;

namespace Schnapp.Api.Auth;

/// <summary>
/// Resolves <c>Authorization: Bearer …</c> into <see cref="HttpContext.User"/> claims for the request.
/// </summary>
/// <remarks>
/// <para>
/// Tray clients and the future Web BFF send long-lived API tokens (not cookies). This middleware
/// looks up the token hash, loads the owning user, and attaches claims so endpoint filters and
/// handlers can authorize without a full ASP.NET Identity cookie stack.
/// </para>
/// <para>
/// Registered in <c>Program.cs</c> via <c>UseMiddleware&lt;BearerTokenMiddleware&gt;()</c> before endpoint execution.
/// Missing or invalid tokens leave the user unauthenticated; protected endpoints then return 401
/// via <see cref="RequireAuthFilter"/>.
/// </para>
/// </remarks>
public sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates the middleware with the next pipeline delegate.
    /// </summary>
    /// <param name="next">Next middleware or endpoint dispatcher.</param>
    public BearerTokenMiddleware(RequestDelegate next) => _next = next;

    /// <summary>
    /// Optionally authenticates the request from a bearer token, then continues the pipeline.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="tokens">Repository used to resolve and touch API tokens.</param>
    /// <param name="users">Repository used to load the token owner and update <c>last_seen</c>.</param>
    /// <returns>Task that completes when the rest of the pipeline has finished.</returns>
    /// <remarks>
    /// Side effects on success: updates token <c>last_used_at</c> and user <c>last_seen</c>
    /// so journal auto-close and ops can see recent activity.
    /// </remarks>
    public async Task InvokeAsync(
        HttpContext context,
        ApiTokenRepository tokens,
        UserRepository users)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var plaintext = header["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(plaintext))
            {
                var hash = TokenHasher.Hash(plaintext);
                var token = await tokens.GetActiveByHashAsync(hash, context.RequestAborted);
                if (token is not null)
                {
                    var user = await users.GetByIdAsync(token.UserId, context.RequestAborted);
                    if (user is not null)
                    {
                        var now = DateTimeOffset.UtcNow;
                        await tokens.TouchLastUsedAsync(token.Id, now, context.RequestAborted);
                        await users.TouchSeenAsync(user.Id, now, context.RequestAborted);

                        var identity = new ClaimsIdentity("Bearer");
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Uuid7Text.Format(user.Id)));
                        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
                        identity.AddClaim(new Claim(ClaimTypes.Name, user.Name));
                        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
                        identity.AddClaim(new Claim("token_id", Uuid7Text.Format(token.Id)));
                        context.User = new ClaimsPrincipal(identity);
                    }
                }
            }
        }

        await _next(context);
    }
}
