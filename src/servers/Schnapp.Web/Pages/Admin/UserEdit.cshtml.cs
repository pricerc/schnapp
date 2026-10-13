using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Admin;

/// <summary>Per-user admin edit page.</summary>
public sealed class UserEditModel : PageModel
{
    private readonly ApiClient _api;
    public UserEditModel(ApiClient api) => _api = api;

    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string UserId { get; private set; } = "";

    [BindProperty]
    public EditModel Input { get; set; } = new();

    public List<string> TimeZoneOptions { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await EnsureAdminAsync(ct)) return Page();
        if (!TryParseId(id, out var userId))
        {
            Error = "Invalid user id.";
            return Page();
        }
        await LoadUserAsync(userId, ct);
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(string id, CancellationToken ct) =>
        SaveCoreAsync(id, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(string id, CancellationToken ct) =>
        SaveCoreAsync(id, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(string id, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await EnsureAdminAsync(ct)) return Page();
        if (!TryParseId(id, out var userId))
        {
            Error = "Invalid user id.";
            return Page();
        }
        UserId = Uuid7Text.Format(userId);

        if (!ModelState.IsValid)
        {
            Error = "Check required fields.";
            return Page();
        }

        var (user, error) = await _api.UpdateUserAsync(userId, new AdminUpdateUserRequest
        {
            Code = Input.Code.Trim(),
            Name = Input.Name.Trim(),
            Email = Input.Email.Trim(),
            Role = Input.Role,
            TimeZoneId = Input.TimeZoneId.Trim(),
            Status = Input.Status,
            ColourCode = string.IsNullOrWhiteSpace(Input.ColourCode) ? null : Input.ColourCode.Trim(),
            Tags = Input.Tags,
            Notes = Input.Notes
        }, ct);

        if (error is not null)
        {
            Error = Friendly(error);
            return Page();
        }

        if (close)
            return RedirectToPage("/Admin/Users");

        Message = "User saved.";
        Apply(user!);
        return Page();
    }

    private async Task<bool> EnsureAdminAsync(CancellationToken ct)
    {
        var me = await _api.GetMeAsync(ct);
        if (me is null)
        {
            AuthCookie.Clear(HttpContext);
            return false;
        }
        IsAdmin = me.Role == "Admin";
        if (!IsAdmin) Error = "Admin role required.";
        return IsAdmin;
    }

    private async Task LoadUserAsync(Uuid7 id, CancellationToken ct)
    {
        // Prefer single-user GET; fall back to directory list (works if API process is older).
        var (user, error) = await _api.GetUserAsync(id, ct);
        if (user is null)
        {
            var (users, listError) = await _api.ListUsersAsync(ct);
            user = users?.FirstOrDefault(u => u.Id == id);
            if (user is null)
            {
                Error = Friendly(error ?? listError ?? "User not found.");
                UserId = Uuid7Text.Format(id);
                return;
            }
        }
        Apply(user);
    }

    private void Apply(UserProfileResponse user)
    {
        UserId = Uuid7Text.Format(user.Id);
        Input = new EditModel
        {
            Code = user.Code,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            TimeZoneId = user.TimeZoneId,
            Status = user.Status,
            ColourCode = user.ColourCode ?? "",
            Tags = user.Tags ?? "",
            Notes = user.Notes ?? ""
        };
    }

    private static bool TryParseId(string? id, out Uuid7 userId) =>
        Uuid7Text.TryParse(id, out userId);

    private static string Friendly(string error)
    {
        try
        {
            using var doc = JsonDocument.Parse(error);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString() ?? error;
        }
        catch { /* plain */ }
        return error;
    }

    public sealed class EditModel
    {
        [Required] public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        public string Role { get; set; } = "User";
        [Required] public string TimeZoneId { get; set; } = "UTC";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
