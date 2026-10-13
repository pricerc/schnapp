namespace Schnapp.Api.Auth;

/// <summary>
/// Endpoint filter that rejects unauthenticated requests with HTTP 401.
/// </summary>
/// <remarks>
/// Applied to route groups in <see cref="AuthEndpoints"/> that require a valid bearer token.
/// Relies on <see cref="BearerTokenMiddleware"/> having set <see cref="HttpContext.User"/>.
/// We intentionally avoid ASP.NET's full authentication scheme stack for MVP simplicity.
/// </remarks>
public sealed class RequireAuthFilter : IEndpointFilter
{
    /// <summary>
    /// Continues only when <see cref="HttpContext.User"/> is authenticated.
    /// </summary>
    /// <param name="context">Endpoint invocation context for the current request.</param>
    /// <param name="next">Next filter or the endpoint handler.</param>
    /// <returns>401 result, or the downstream endpoint result.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        return await next(context);
    }
}

/// <summary>
/// Endpoint filter that requires an authenticated admin user (HTTP 401 / 403 otherwise).
/// </summary>
/// <remarks>
/// Used for <c>/api/v1/admin/*</c> routes. Non-admins receive 403 so clients can distinguish
/// "not logged in" from "logged in but not allowed".
/// </remarks>
public sealed class RequireAdminFilter : IEndpointFilter
{
    /// <summary>
    /// Continues only when the caller is authenticated and <see cref="AuthUser.IsAdmin"/> is true.
    /// </summary>
    /// <param name="context">Endpoint invocation context for the current request.</param>
    /// <param name="next">Next filter or the endpoint handler.</param>
    /// <returns>401, 403, or the downstream endpoint result.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        var auth = AuthUser.FromPrincipal(user);
        if (!auth.IsAdmin)
            return Results.Json(new { error = "Admin role required." }, statusCode: StatusCodes.Status403Forbidden);
        return await next(context);
    }
}
