using System.Security.Claims;
using Schnapp.Api.Tags;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Auth;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Api.Auth;

/// <summary>
/// Minimal API route map for authentication, profile, and admin user provisioning.
/// </summary>
/// <remarks>
/// <para>
/// Called once from <c>Program.cs</c> via <see cref="MapAuthEndpoints"/>. Public login is unauthenticated;
/// other routes use <see cref="RequireAuthFilter"/> or <see cref="RequireAdminFilter"/>.
/// </para>
/// <list type="table">
/// <listheader><term>Route</term><description>Purpose</description></listheader>
/// <item><term><c>POST /api/v1/auth/login</c></term><description>Email/password → bearer token (plaintext once).</description></item>
/// <item><term><c>POST /api/v1/auth/tokens</c></term><description>Issue a named long-lived token for tray clients.</description></item>
/// <item><term><c>DELETE /api/v1/auth/tokens/{id}</c></term><description>Revoke a token owned by the caller.</description></item>
/// <item><term><c>GET/PATCH /api/v1/me</c></term><description>Read/update the caller's profile (incl. timezone).</description></item>
    /// <item><term><c>GET /api/v1/admin/users</c></term><description>Admin lists all user profiles.</description></item>
    /// <item><term><c>POST /api/v1/admin/users</c></term><description>Admin creates a colleague account.</description></item>
    /// <item><term><c>GET/PATCH/DELETE /api/v1/admin/users/{id}</c></term><description>Admin get/update/delete a user.</description></item>
    /// <item><term><c>POST /api/v1/admin/users/{id}/reset-password</c></term><description>Admin sets a new password hash.</description></item>
/// </list>
/// </remarks>
public static class AuthEndpoints
{
    /// <summary>
    /// Registers Phase 2 auth and admin endpoints on the application.
    /// </summary>
    /// <param name="app">The endpoint route builder (usually the <see cref="WebApplication"/>).</param>
    /// <returns>The same <paramref name="app"/> for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/auth/login", LoginAsync);

        var authed = api.MapGroup("").AddEndpointFilter<RequireAuthFilter>();
        authed.MapPost("/auth/tokens", CreateTokenAsync);
        authed.MapDelete("/auth/tokens/{id}", RevokeTokenAsync);
        authed.MapGet("/me", GetMeAsync);
        authed.MapPatch("/me", UpdateMeAsync);

        var admin = api.MapGroup("").AddEndpointFilter<RequireAdminFilter>();
        admin.MapGet("/admin/users", ListUsersAsync);
        admin.MapPost("/admin/users", CreateUserAsync);
        admin.MapGet("/admin/users/{id}", GetUserAsync);
        admin.MapPatch("/admin/users/{id}", UpdateUserAsync);
        admin.MapDelete("/admin/users/{id}", DeleteUserAsync);
        admin.MapPost("/admin/users/{id}/reset-password", ResetPasswordAsync);

        return app;
    }

    /// <summary>
    /// Validates identity (email, code, or name) and optional password; issues a new API token named <c>login</c>.
    /// </summary>
    /// <param name="request">Credentials from the client, Web BFF, or CLI.</param>
    /// <param name="users">User lookup and last-login update.</param>
    /// <param name="tokens">Persists the new token hash.</param>
    /// <param name="passwords">Verifies the password (or passwordless empty hash).</param>
    /// <param name="ct">Cancellation token for the request.</param>
    /// <returns>
    /// 200 with <see cref="LoginResponse"/> (plaintext token once), 400 if identity missing, 401 if invalid.
    /// </returns>
    /// <remarks>
    /// Identity may be unique email, code, or name. Password is optional for accounts with no stored hash
    /// (trusted deployments). Failure modes return a generic 401 message.
    /// </remarks>
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserRepository users,
        ApiTokenRepository tokens,
        PasswordService passwords,
        CancellationToken ct)
    {
        var identity = request.GetIdentity();
        if (string.IsNullOrWhiteSpace(identity))
            return Results.BadRequest(new { error = "Login identity (email, code, or name) is required." });

        var user = await users.GetByLoginIdentityAsync(identity, ct);
        if (user is null || !passwords.Verify(user, request.Password))
            return Results.Json(new { error = "Invalid login or password." }, statusCode: StatusCodes.Status401Unauthorized);

        var now = DateTimeOffset.UtcNow;
        await users.TouchLoginAsync(user.Id, now, ct);

        var plaintext = TokenHasher.CreatePlaintextToken();
        var token = new ApiToken
        {
            Id = IdFactory.NewId(),
            UserId = user.Id,
            TokenHash = TokenHasher.Hash(plaintext),
            Name = "login",
            CreatedAt = now
        };
        await tokens.InsertAsync(token, ct);

        return Results.Ok(new LoginResponse
        {
            Token = plaintext,
            TokenId = token.Id,
            User = ToProfile(user)
        });
    }

    /// <summary>
    /// Issues an additional named bearer token for the authenticated user (e.g. tray client install).
    /// </summary>
    /// <param name="request">Display name and optional notes for the token row.</param>
    /// <param name="principal">Current user claims from <see cref="BearerTokenMiddleware"/>.</param>
    /// <param name="tokens">Token insert repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with plaintext token once; 400 if name missing.</returns>
    private static async Task<IResult> CreateTokenAsync(
        CreateTokenRequest request,
        ClaimsPrincipal principal,
        ApiTokenRepository tokens,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        var plaintext = TokenHasher.CreatePlaintextToken();
        var token = new ApiToken
        {
            Id = IdFactory.NewId(),
            UserId = auth.Id,
            TokenHash = TokenHasher.Hash(plaintext),
            Name = request.Name.Trim(),
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await tokens.InsertAsync(token, ct);

        return Results.Ok(new CreateTokenResponse
        {
            Id = token.Id,
            Name = token.Name,
            Token = plaintext,
            CreatedAt = token.CreatedAt
        });
    }

    /// <summary>
    /// Soft-revokes a token owned by the caller so it can no longer authenticate.
    /// </summary>
    /// <param name="id">Token row id (not the plaintext secret).</param>
    /// <param name="principal">Current user; must own the token.</param>
    /// <param name="tokens">Token revoke repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 if revoked; 404 if not found or not owned by caller.</returns>
    private static async Task<IResult> RevokeTokenAsync(
        Uuid7Param id,
        ClaimsPrincipal principal,
        ApiTokenRepository tokens,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var revoked = await tokens.RevokeAsync(id, auth.Id, DateTimeOffset.UtcNow, ct);
        return revoked ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Returns the authenticated user's public profile fields.
    /// </summary>
    /// <param name="principal">Current user claims.</param>
    /// <param name="users">User load repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with <see cref="UserProfileResponse"/>, or 401 if the user row disappeared.</returns>
    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        UserRepository users,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(ToProfile(user));
    }

    /// <summary>
    /// Partially updates the caller's profile (code, name, timezone, tags, notes).
    /// </summary>
    /// <param name="request">Fields to change; null properties mean "leave unchanged".</param>
    /// <param name="principal">Current user claims.</param>
    /// <param name="users">User update repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with updated profile; 400/401/409 on validation or uniqueness failure.</returns>
    /// <remarks>
    /// Timezone updates matter for day-browser URLs (<c>/yyyy/MM/dd/</c>) in the Web UI.
    /// </remarks>
    private static async Task<IResult> UpdateMeAsync(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        UserRepository users,
        ListRepository lists,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null)
            return Results.Unauthorized();

        if (request.Code is not null)
            user.Code = request.Code.Trim();
        if (request.Name is not null)
            user.Name = request.Name.Trim();
        if (request.TimeZoneId is not null)
        {
            var tz = request.TimeZoneId.Trim();
            if (!IsValidTimeZoneId(tz))
                return Results.BadRequest(new { error = $"Unknown time zone id '{tz}'.", field = "timeZoneId" });
            user.TimeZoneId = tz;
        }
        if (request.Tags is not null)
        {
            var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, request.Tags, customerId: null, ct);
            user.Tags = string.IsNullOrEmpty(tags) ? null : tags;
        }
        if (request.Notes is not null)
            user.Notes = request.Notes;
        if (request.Status is not null)
            user.Status = request.Status.Value;
        if (request.ColourCode is not null)
            user.ColourCode = request.ColourCode;

        if (string.IsNullOrWhiteSpace(user.Code) || string.IsNullOrWhiteSpace(user.Name))
            return Results.BadRequest(new { error = "Code and Name cannot be empty." });

        user.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await users.UpdateProfileAsync(user, ct);
        }
        catch (Exception)
        {
            return Results.Conflict(new { error = "Code or Name already in use." });
        }

        return Results.Ok(ToProfile(user));
    }

    /// <summary>
    /// Admin-only: lists all user profiles for the admin users page.
    /// </summary>
    /// <param name="users">User list repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with an array of <see cref="UserProfileResponse"/>.</returns>
    /// <remarks>
    /// Authorization is enforced by <see cref="RequireAdminFilter"/> on the route group.
    /// </remarks>
    private static async Task<IResult> ListUsersAsync(
        UserRepository users,
        CancellationToken ct)
    {
        var list = await users.ListAsync(ct);
        return Results.Ok(list.Select(ToProfile).ToList());
    }

    /// <summary>
    /// Admin-only: creates a new org user with hashed password (no public self-signup).
    /// </summary>
    /// <param name="request">Email, password, name, optional code/role/timezone.</param>
    /// <param name="users">User insert repository.</param>
    /// <param name="passwords">Password hashing service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 with profile; 400/409 on validation or uniqueness conflicts.</returns>
    /// <remarks>
    /// Authorization is enforced by <see cref="RequireAdminFilter"/> on the route group.
    /// </remarks>
    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        UserRepository users,
        PasswordService passwords,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Email and name are required." });

        var role = request.Role is UserRole.Admin or UserRole.User ? request.Role : UserRole.User;
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? DeriveCode(request.Email)
            : request.Code.Trim();

        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId.Trim();
        if (!IsValidTimeZoneId(timeZoneId))
            return Results.BadRequest(new { error = $"Unknown time zone id '{timeZoneId}'.", field = "timeZoneId" });

        if (await users.GetByEmailAsync(request.Email.Trim(), ct) is not null)
            return Results.Conflict(new { error = "Email already registered.", field = "email" });

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = IdFactory.NewId(),
            Email = request.Email.Trim(),
            Code = code,
            Name = request.Name.Trim(),
            Role = role,
            TimeZoneId = timeZoneId,
            Status = request.Status,
            ColourCode = request.ColourCode,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwords.HashPasswordOrEmpty(user, request.Password);

        try
        {
            await users.InsertAsync(user, ct);
        }
        catch (Exception)
        {
            return Results.Conflict(new { error = "User with that email, code, or name already exists." });
        }

        return Results.Created($"/api/v1/admin/users/{Uuid7Text.Format(user.Id)}", ToProfile(user));
    }

    /// <summary>
    /// Admin-only: replaces a user's password hash without knowing the old password.
    /// </summary>
    /// <param name="id">Target user id.</param>
    /// <param name="request">New plaintext password.</param>
    /// <param name="users">User load and password update repository.</param>
    /// <param name="passwords">Password hashing service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 on success; 400/404 otherwise.</returns>
    private static async Task<IResult> ResetPasswordAsync(
        Uuid7Param id,
        ResetPasswordRequest request,
        UserRepository users,
        PasswordService passwords,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { error = "Password is required." });

        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return Results.NotFound();

        var hash = passwords.HashPassword(user, request.Password);
        await users.UpdatePasswordHashAsync(user.Id, hash, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetUserAsync(Uuid7Param id, UserRepository users, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct);
        return user is null ? Results.NotFound() : Results.Ok(ToProfile(user));
    }

    private static async Task<IResult> UpdateUserAsync(
        Uuid7Param id,
        AdminUpdateUserRequest request,
        ClaimsPrincipal principal,
        UserRepository users,
        ListRepository lists,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(new { error = "Code, name, and email are required." });

        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return Results.NotFound();

        var role = request.Role is UserRole.Admin or UserRole.User ? request.Role : UserRole.User;
        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId.Trim();
        if (!IsValidTimeZoneId(timeZoneId))
            return Results.BadRequest(new { error = $"Unknown time zone id '{timeZoneId}'.", field = "timeZoneId" });

        // Prevent demoting the last admin (including self).
        if (user.Role == UserRole.Admin && role != UserRole.Admin)
        {
            var admins = await users.CountAdminsAsync(ct);
            if (admins <= 1)
                return Results.BadRequest(new { error = "Cannot demote the last admin." });
        }

        var email = request.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await users.GetByEmailAsync(email, ct);
            if (existing is not null && existing.Id != user.Id)
                return Results.Conflict(new { error = "Email already registered.", field = "email" });
        }

        user.Code = request.Code.Trim();
        user.Name = request.Name.Trim();
        user.Email = email;
        user.Role = role;
        user.TimeZoneId = timeZoneId;
        user.Status = request.Status;
        user.ColourCode = request.ColourCode;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, request.Tags, customerId: null, ct);
        user.Tags = string.IsNullOrEmpty(tags) ? null : tags;
        user.Notes = request.Notes;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await users.UpdateAdminAsync(user, ct);
        }
        catch (Exception)
        {
            return Results.Conflict(new { error = "User with that email, code, or name already exists." });
        }

        return Results.Ok(ToProfile(user));
    }

    private static async Task<IResult> DeleteUserAsync(
        Uuid7Param id,
        ClaimsPrincipal principal,
        UserRepository users,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        if (auth.Id == (Uuid7)id)
            return Results.BadRequest(new { error = "Cannot delete your own account." });

        var user = await users.GetByIdAsync(id, ct);
        if (user is null)
            return Results.NotFound();

        if (user.Role == UserRole.Admin)
        {
            var admins = await users.CountAdminsAsync(ct);
            if (admins <= 1)
                return Results.BadRequest(new { error = "Cannot delete the last admin." });
        }

        await users.DeleteAsync(id, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Maps a domain <see cref="User"/> to the public API profile DTO (no password hash).
    /// </summary>
    /// <param name="user">Persisted user entity.</param>
    /// <returns>Safe profile response for JSON serialization.</returns>
    private static UserProfileResponse ToProfile(User user) => new()
    {
        Id = user.Id,
        Code = user.Code,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        TimeZoneId = user.TimeZoneId,
        Tags = user.Tags,
        Notes = user.Notes,
        Status = user.Status,
        ColourCode = user.ColourCode,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    /// <summary>
    /// Derives a short unique-ish <see cref="User.Code"/> from the email local-part when admin omits code.
    /// </summary>
    /// <param name="email">Email address being registered.</param>
    /// <returns>Alphanumeric code (max 32 chars), or <c>user</c> if nothing usable remains.</returns>
    private static string DeriveCode(string email)
    {
        var local = email.Split('@')[0];
        var cleaned = new string(local.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "user";
        return cleaned.Length > 32 ? cleaned[..32] : cleaned;
    }

    /// <summary>
    /// Checks whether <paramref name="timeZoneId"/> is a known IANA/system time zone id.
    /// </summary>
    /// <remarks>
    /// Invalid ids previously stored silently and only surfaced later as a silent fallback to
    /// UTC in <see cref="Screenshots.DayRange"/>, making the setting look like it was ignored.
    /// </remarks>
    private static bool IsValidTimeZoneId(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
