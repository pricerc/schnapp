using Microsoft.AspNetCore.Mvc;
using Schnapp.Contracts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Screenshots;

/// <summary>
/// Proxies thumbnail bytes from the API so browser img tags need not send Authorization headers.
/// </summary>
public sealed class ThumbModel : PageModel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ThumbModel> _logger;

    public ThumbModel(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ThumbModel> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public Task<IActionResult> OnGetAsync(Uuid7Param id, int? size, CancellationToken ct) =>
        ScreenshotImageProxy.GetAsync(this, _httpFactory, _config, _logger, id, "thumb", "image/jpeg", ct, size);
}
