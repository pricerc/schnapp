using System.Security.Cryptography;
using System.Text;

namespace Schnapp.Domain.Auth;

/// <summary>
/// Creates and hashes opaque API bearer tokens without depending on ASP.NET types.
/// </summary>
/// <remarks>
/// Shared by API login/token endpoints and <c>BearerTokenMiddleware</c>.
/// Plaintext is random URL-safe base64; only the SHA-256 hex digest is stored.
/// </remarks>
public static class TokenHasher
{
    /// <summary>
    /// Generates a new high-entropy plaintext token for the client to store.
    /// </summary>
    /// <returns>URL-safe base64 string (no padding).</returns>
    /// <remarks>
    /// Callers must show this value once, then persist only <see cref="Hash"/> of it.
    /// </remarks>
    public static string CreatePlaintextToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Computes the stable database lookup key for a plaintext token.
    /// </summary>
    /// <param name="plaintextToken">Secret presented in the <c>Authorization: Bearer</c> header.</param>
    /// <returns>Lowercase hex SHA-256 digest.</returns>
    public static string Hash(string plaintextToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
