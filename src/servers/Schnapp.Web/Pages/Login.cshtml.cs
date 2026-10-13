using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Web.Services;

namespace Schnapp.Web.Pages;

/// <summary>
/// Browser login against the API; stores the returned bearer token in an encrypted cookie.
/// </summary>
/// <remarks>
/// Manual Phase 2 test entry point. Called when the user opens <c>/Login</c>.
/// </remarks>
public sealed class LoginModel : PageModel
{
    private readonly ApiClient _api;

    /// <summary>
    /// Creates the login page model.
    /// </summary>
    /// <param name="api">BFF client used to call <c>POST /api/v1/auth/login</c>.</param>
    public LoginModel(ApiClient api) => _api = api;

    /// <summary>Bound form fields.</summary>
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Error message shown on failed login.</summary>
    public string? Error { get; private set; }

    /// <summary>GET: show form; redirect home if already signed in.</summary>
    public IActionResult OnGet()
    {
        if (AuthCookie.GetToken(HttpContext) is not null)
            return RedirectToPage("/Index");
        return Page();
    }

    /// <summary>
    /// POST: authenticate via API and set the auth cookie.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var result = await _api.LoginAsync(Input.Login.Trim(), Input.Password, ct);
            if (result is null)
            {
                Error = "Invalid login or password.";
                return Page();
            }

            AuthCookie.SetToken(HttpContext, result.Token);
            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            Error = "Could not reach API: " + ex.Message;
            return Page();
        }
    }

    /// <summary>Login form fields.</summary>
    public sealed class InputModel
    {
        /// <summary>Email, unique code, or unique name.</summary>
        [Required]
        public string Login { get; set; } = "";

        /// <summary>Password (optional for passwordless accounts).</summary>
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }
}
