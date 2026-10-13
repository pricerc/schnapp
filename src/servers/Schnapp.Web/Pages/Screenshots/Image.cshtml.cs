using Microsoft.AspNetCore.Mvc;
using Schnapp.Contracts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Screenshots;

/// <summary>Proxies full image bytes from the API for browser display.</summary>
public sealed class ImageModel : PageModel
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ImageModel> _logger;

    public ImageModel(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ImageModel> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public Task<IActionResult> OnGetAsync(Uuid7Param id, CancellationToken ct) =>
        ScreenshotImageProxy.GetAsync(this, _httpFactory, _config, _logger, id, "image", "application/octet-stream", ct);
}
