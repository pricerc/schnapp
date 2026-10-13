using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Net.Http.Headers;
using Schnapp.Contracts;

using Medo;
namespace Schnapp.Web.Services;

/// <summary>
/// Streams screenshot image/thumb bytes from the API with the caller's bearer token,
/// forwarding conditional headers so browser caches revalidate with 304s.
/// </summary>
internal static class ScreenshotImageProxy
{
    /// <param name="kind">API sub-path: <c>image</c> or <c>thumb</c>.</param>
    /// <param name="size">Thumb tier edge to request (thumb only).</param>
    public static async Task<IActionResult> GetAsync(
        PageModel page, IHttpClientFactory httpFactory, IConfiguration config, ILogger logger,
        Uuid7Param id, string kind, string fallbackContentType, CancellationToken ct, int? size = null)
    {
        var idText = Uuid7Text.Format(id);
        var token = AuthCookie.GetToken(page.HttpContext);
        if (token is null)
        {
            logger.LogWarning("Get {Kind} {Id}: no auth cookie/token present, returning 401", kind, idText);
            return page.Unauthorized();
        }

        var baseUrl = (config["Schnapp:ApiBaseUrl"] ?? "http://localhost:5080").TrimEnd('/');
        var url = $"{baseUrl}/api/v1/screenshots/{idText}/{kind}" + (size is { } s ? $"?size={s}" : "");
        var client = httpFactory.CreateClient("SchnappApi");
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        foreach (var name in new[] { HeaderNames.IfNoneMatch, HeaderNames.IfModifiedSince })
        {
            if (page.Request.Headers.TryGetValue(name, out var v))
                req.Headers.TryAddWithoutValidation(name, (string?)v);
        }

        var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        page.Response.RegisterForDispose(req);
        page.Response.RegisterForDispose(res);

        if (res.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            ApplyCacheHeaders(page, res);
            return page.StatusCode(StatusCodes.Status304NotModified);
        }
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("Get {Kind} {Id}: API returned {StatusCode} for {Url}", kind, idText, (int)res.StatusCode, url);
            return page.StatusCode((int)res.StatusCode);
        }

        page.Response.Headers.CacheControl = CacheControlOf(res);
        var etag = res.Headers.ETag is { } e ? new EntityTagHeaderValue(e.Tag.ToString()) : null;
        var stream = await res.Content.ReadAsStreamAsync(ct);
        return new FileStreamResult(stream, res.Content.Headers.ContentType?.MediaType ?? fallbackContentType)
        {
            LastModified = res.Content.Headers.LastModified,
            EntityTag = etag
        };
    }

    // The API sends no-cache while a requested thumb tier is still missing; keep that.
    private static string CacheControlOf(HttpResponseMessage res) =>
        res.Headers.CacheControl?.ToString() ?? "private, max-age=86400";

    private static void ApplyCacheHeaders(PageModel page, HttpResponseMessage res)
    {
        page.Response.Headers.CacheControl = CacheControlOf(res);
        if (res.Headers.ETag is { } e)
            page.Response.Headers.ETag = e.ToString();
        if (res.Content.Headers.LastModified is { } lm)
            page.Response.Headers.LastModified = lm.ToString("R");
    }
}
