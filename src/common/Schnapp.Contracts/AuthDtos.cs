namespace Schnapp.Contracts;

using Medo;

/// <summary>Request body for <c>POST /api/v1/auth/login</c>.</summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Login identity: unique <c>email</c>, <c>code</c>, or <c>name</c> (case-insensitive).
    /// Preferred field for new clients.
    /// </summary>
    public string? Login { get; set; }

    /// <summary>
    /// Alias for <see cref="Login"/> (backward compatible with older clients that only sent email).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Plaintext password; optional in trusted deployments when the account has no password.
    /// Never logged or stored.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>Resolves the identity string from <see cref="Login"/> or <see cref="Email"/>.</summary>
    public string? GetIdentity()
    {
        #pragma warning disable CS8602 // null dereference: we check for null/whitespace
        if (!string.IsNullOrWhiteSpace(Login))
        {
            return Login.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Email))
        {
            return Email.Trim();
        }
        #pragma warning restore CS8602 // null dereference: we check for null/whitespace
        return null;
    }

}

/// <summary>Successful login payload; <see cref="Token"/> is shown only in this response.</summary>
public sealed class LoginResponse
{
    /// <summary>Plaintext bearer secret for subsequent <c>Authorization</c> headers.</summary>
    public string Token { get; set; } = "";

    /// <summary>Database id of the new <c>api_token</c> row (for revoke).</summary>
    public Uuid7 TokenId { get; set; }

    /// <summary>Public profile of the authenticated user.</summary>
    public UserProfileResponse User { get; set; } = new();
}

/// <summary>Request body for <c>POST /api/v1/auth/tokens</c>.</summary>
public sealed class CreateTokenRequest
{
    /// <summary>Human-readable token label (e.g. machine name).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional free-text notes.</summary>
    public string? Notes { get; set; }
}

/// <summary>Response after creating a named token; plaintext appears only here.</summary>
public sealed class CreateTokenResponse
{
    /// <summary>Token row id.</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Label stored on the token.</summary>
    public string Name { get; set; } = "";

    /// <summary>Plaintext bearer secret (store client-side; not recoverable later).</summary>
    public string Token { get; set; } = "";

    /// <summary>When the token was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Public user profile returned by login, <c>/me</c>, and admin create.</summary>
public sealed class UserProfileResponse
{
    /// <summary>User id.</summary>
    public Uuid7 Id { get; set; }

    /// <summary>Short unique code.</summary>
    public string Code { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Login email.</summary>
    public string Email { get; set; } = "";

    /// <summary>Role string (<c>User</c> or <c>Admin</c>).</summary>
    public string Role { get; set; } = "";

    /// <summary>IANA timezone used for day-browser URLs.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Optional tags free text.</summary>
    public string? Tags { get; set; }

    /// <summary>Optional notes free text.</summary>
    public string? Notes { get; set; }

    /// <summary>User status code.</summary>
    public int Status { get; set; }

    /// <summary>Optional display colour.</summary>
    public string? ColourCode { get; set; }

    /// <summary>When the user was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the user profile was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Partial update for <c>PATCH /api/v1/me</c>; null means leave unchanged.</summary>
public sealed class UpdateProfileRequest
{
    /// <summary>New code, or null to keep current.</summary>
    public string? Code { get; set; }

    /// <summary>New display name, or null to keep current.</summary>
    public string? Name { get; set; }

    /// <summary>New IANA timezone, or null to keep current.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>New tags, or null to keep current.</summary>
    public string? Tags { get; set; }

    /// <summary>New notes, or null to keep current.</summary>
    public string? Notes { get; set; }

    /// <summary>New status, or null to keep current.</summary>
    public int? Status { get; set; }

    /// <summary>New colour code, or null to keep current.</summary>
    public string? ColourCode { get; set; }
}

/// <summary>Admin request to create a colleague account.</summary>
public sealed class CreateUserRequest
{
    /// <summary>Unique login email.</summary>
    public string Email { get; set; } = "";

    /// <summary>Initial plaintext password (hashed before storage); empty = passwordless account.</summary>
    public string? Password { get; set; }

    /// <summary>Display name (unique).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional short code; derived from email if omitted.</summary>
    public string? Code { get; set; }

    /// <summary><c>User</c> (default) or <c>Admin</c>.</summary>
    public string Role { get; set; } = "User";

    /// <summary>Optional IANA timezone; defaults to UTC.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>User status code (default 0 = Active).</summary>
    public int Status { get; set; }

    /// <summary>Optional display colour.</summary>
    public string? ColourCode { get; set; }
}

/// <summary>Admin request to set a new password for a user.</summary>
public sealed class ResetPasswordRequest
{
    /// <summary>New plaintext password.</summary>
    public string Password { get; set; } = "";
}

/// <summary>Admin full update for a user row (directory grid / edit page).</summary>
public sealed class AdminUpdateUserRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary><c>User</c> or <c>Admin</c>.</summary>
    public string Role { get; set; } = "User";

    /// <summary>IANA timezone id.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>User status code (0 = Active, 1 = Inactive).</summary>
    public int Status { get; set; }

    public string? ColourCode { get; set; }
    public string? Tags { get; set; }
    public string? Notes { get; set; }
}
