using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;

namespace Schnapp.Web.Pages.Api;

/// <summary>
/// BFF proxy for tag dictionary autocomplete (<c>GET /Api/Tags</c>).
/// </summary>
public class TagsModel : PageModel
{
    private readonly ApiClient _api;

    public TagsModel(ApiClient api) => _api = api;

    /// <summary>
    /// Returns tag dictionary JSON: global tags, optionally plus a customer's tags.
    /// </summary>
    /// <param name="customerId">Optional customer scope (Id26 or RFC UUID).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IActionResult> OnGetAsync(string? customerId, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return new JsonResult(Array.Empty<TagResponse>()) { StatusCode = StatusCodes.Status401Unauthorized };

        Uuid7? cid = null;
        if (!string.IsNullOrWhiteSpace(customerId) && Uuid7Text.TryParse(customerId, out var parsed)
            && parsed != Uuid7.Empty)
            cid = parsed;

        var tags = await _api.ListTagsAsync(cid, scope: null, ct) ?? new List<TagResponse>();
        // Distinct tag text for autocomplete (prefer first occurrence; global listed first by API).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>();
        foreach (var t in tags)
        {
            if (string.IsNullOrEmpty(t.Tag)) continue;
            if (seen.Add(t.Tag))
                values.Add(t.Tag);
        }

        return new JsonResult(new { tags = values });
    }
}
