using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;

namespace Schnapp.Web.Pages.Api;

/// <summary>BFF proxy: mark idle/blank screenshot duplicates for the Home gallery.</summary>
[IgnoreAntiforgeryToken]
public sealed class ScreenshotDuplicatesModel : PageModel
{
    private readonly ApiClient _api;

    public ScreenshotDuplicatesModel(ApiClient api) => _api = api;

    public async Task<IActionResult> OnPostAsync([FromBody] MarkBody? body, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
        {
            return new JsonResult(new { error = "Not signed in." }) { StatusCode = StatusCodes.Status401Unauthorized };
        }

        var ids = new List<Uuid7>();
        if (body?.Ids != null)
        {
            foreach (var raw in body.Ids)
            {
                if (Uuid7Text.TryParse(raw, out var id) && id != Uuid7.Empty)
                {
                    ids.Add(id);
                }
            }
        }

        var max = body?.MaxDirtyCells ?? 2;
        var result = await _api.FindScreenshotDuplicatesAsync(ids, max, ct);
        if (result is null)
        {
            return new JsonResult(new { error = "Could not scan duplicates." })
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }

        return new JsonResult(result);
    }

    public sealed class MarkBody
    {
        public List<string>? Ids { get; set; }
        public int MaxDirtyCells { get; set; } = 2;
    }
}
