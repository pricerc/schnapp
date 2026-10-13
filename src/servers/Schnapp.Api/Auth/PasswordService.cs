using Microsoft.AspNetCore.Identity;
using Schnapp.Domain.Entities;

namespace Schnapp.Api.Auth;

/// <summary>
/// Hashes and verifies user passwords using ASP.NET Identity's <see cref="PasswordHasher{TUser}"/>.
/// </summary>
/// <remarks>
/// Registered as a singleton in <c>Program.cs</c>. Called from login, admin user create,
/// password reset, and <see cref="AdminBootstrap"/>. Passwords are never stored in plaintext.
/// </remarks>
public sealed class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    /// <summary>
    /// Creates a salted one-way hash suitable for storage in <see cref="User.PasswordHash"/>.
    /// </summary>
    /// <param name="user">User entity used as Identity hasher context (id/email not required to be persisted yet).</param>
    /// <param name="password">Plaintext password supplied by the caller; not retained.</param>
    /// <returns>Opaque hash string for the database.</returns>
    public string HashPassword(User user, string password) =>
        _hasher.HashPassword(user, password);

    /// <summary>
    /// Checks a plaintext password against the stored hash on <paramref name="user"/>.
    /// </summary>
    /// <param name="user">User loaded from the database, including <see cref="User.PasswordHash"/>.</param>
    /// <param name="password">Plaintext password from the login request; may be null/empty for passwordless accounts.</param>
    /// <returns>
    /// <see langword="true"/> if credentials are acceptable; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Passwordless accounts store an empty <see cref="User.PasswordHash"/>. Login with a null/empty
    /// password succeeds only for those accounts. Accounts with a real hash always require a matching password.
    /// </para>
    /// Callers should treat false the same as "user not found" to avoid account enumeration where practical.
    /// </remarks>
    public bool Verify(User user, string? password)
    {
        var hasStoredPassword = !string.IsNullOrEmpty(user.PasswordHash);
        var provided = password ?? "";

        if (!hasStoredPassword)
            return string.IsNullOrEmpty(provided);

        if (string.IsNullOrEmpty(provided))
            return false;

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, provided)
            is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <summary>
    /// Returns a hash for storage, or empty string when the password is null/empty (passwordless account).
    /// </summary>
    /// <param name="user">User entity used as Identity hasher context.</param>
    /// <param name="password">Plaintext password, or null/empty for no password.</param>
    public string HashPasswordOrEmpty(User user, string? password) =>
        string.IsNullOrEmpty(password) ? "" : HashPassword(user, password);
}
