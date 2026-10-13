namespace Schnapp.Web.Services;

/// <summary>
/// Helpers for the HTTP-only cookie that holds the API bearer token in the browser session.
/// </summary>
/// <remarks>
/// The Web app never puts the token in localStorage; it stays in an HTTP-only cookie so Razor
/// pages can call the API as a BFF. Temporary UI may still <em>display</em> the token for manual testing.
/// (Cookie value is the API token itself; not browser-readable via JS. Further encryption can be added later.)
/// </remarks>
public static class AuthCookie
{
    /// <summary>Cookie name for the opaque API token.</summary>
    public const string Name = "schnapp_api_token";

    /// <summary>
    /// Stores the API bearer token in an HTTP-only cookie.
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    /// <param name="token">Plaintext bearer token from login.</param>
    public static void SetToken(HttpContext context, string token)
    {
        context.Response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30)
        });
    }

    /// <summary>
    /// Reads the API token from the request cookie, if present.
    /// </summary>
    /// <param name="context">Current HTTP context (may be null outside a request).</param>
    /// <returns>Token string, or null.</returns>
    public static string? GetToken(HttpContext? context)
    {
        if (context is null)
            return null;
        return context.Request.Cookies.TryGetValue(Name, out var token) ? token : null;
    }

    /// <summary>
    /// Clears the auth cookie (logout).
    /// </summary>
    /// <param name="context">Current HTTP context.</param>
    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(Name);
    }
}
