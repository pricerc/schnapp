using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Admin;

/// <summary>Admin users directory: editable grid, create, password, delete.</summary>
public sealed class UsersModel : PageModel
{
    private readonly ApiClient _api;
    public UsersModel(ApiClient api) => _api = api;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public RowModel Row { get; set; } = new();

    [BindProperty]
    public ResetModel Reset { get; set; } = new();

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public bool IsAdmin { get; private set; }
    public Uuid7 CurrentUserId { get; private set; }
    public IReadOnlyList<UserProfileResponse> Users { get; private set; } = [];

    public List<string> TimeZoneOptions { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        if (!IsAdmin) { Error = "Admin role required."; return Page(); }
        if (!TryValidateModel(Input, nameof(Input)))
        {
            Error = "Check required fields for create.";
            return Page();
        }

        var (profile, error) = await _api.CreateUserAsync(new CreateUserRequest
        {
            Email = Input.Email.Trim(),
            Password = Input.Password,
            Name = Input.Name.Trim(),
            Code = string.IsNullOrWhiteSpace(Input.Code) ? null : Input.Code.Trim(),
            Role = Input.Role,
            TimeZoneId = string.IsNullOrWhiteSpace(Input.TimeZoneId) ? null : Input.TimeZoneId.Trim()
        }, ct);

        if (error is not null) Error = Friendly(error);
        else
        {
            Message = $"Created {profile!.Name} ({profile.Email}).";
            Input = new InputModel { TimeZoneId = Input.TimeZoneId };
        }
        await LoadUsersAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveRowAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        if (!IsAdmin) { Error = "Admin role required."; return Page(); }
        if (!Uuid7Text.TryParse(Row.Id, out var id))
        {
            Error = "Invalid user id.";
            return Page();
        }

        var (user, error) = await _api.UpdateUserAsync(id, new AdminUpdateUserRequest
        {
            Code = Row.Code.Trim(),
            Name = Row.Name.Trim(),
            Email = Row.Email.Trim(),
            Role = Row.Role,
            TimeZoneId = Row.TimeZoneId.Trim(),
            Status = Row.Status
        }, ct);

        if (error is not null) Error = Friendly(error);
        else Message = $"Saved {user!.Name}.";
        await LoadUsersAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        if (!IsAdmin) { Error = "Admin role required."; return Page(); }
        if (!Uuid7Text.TryParse(Row.Id, out var id))
        {
            Error = "Invalid user id.";
            return Page();
        }

        var error = await _api.DeleteUserAsync(id, ct);
        if (error is not null) Error = Friendly(error);
        else Message = "User deleted.";
        await LoadUsersAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        if (!IsAdmin) { Error = "Admin role required."; return Page(); }
        if (!Uuid7Text.TryParse(Reset.UserId, out var id))
        {
            Error = "Invalid user id.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(Reset.Password) || Reset.Password.Length < 6)
        {
            Error = "Password must be at least 6 characters.";
            return Page();
        }

        var error = await _api.ResetUserPasswordAsync(id, Reset.Password, ct);
        if (error is not null) Error = Friendly(error);
        else Message = "Password updated.";
        await LoadUsersAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var me = await _api.GetMeAsync(ct);
        if (me is null)
        {
            AuthCookie.Clear(HttpContext);
            return;
        }
        IsAdmin = me.Role == "Admin";
        CurrentUserId = me.Id;
        if (IsAdmin)
            await LoadUsersAsync(ct);
    }

    private async Task LoadUsersAsync(CancellationToken ct)
    {
        var (users, error) = await _api.ListUsersAsync(ct);
        if (error is not null)
            Error ??= Friendly(error);
        else
            Users = users ?? [];
    }

    private static string Friendly(string error)
    {
        try
        {
            using var doc = JsonDocument.Parse(error);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString() ?? error;
        }
        catch { /* plain text */ }
        return error;
    }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, MinLength(6)]
        public string Password { get; set; } = "";

        [Required]
        public string Name { get; set; } = "";

        public string Code { get; set; } = "";
        public string Role { get; set; } = "User";
        public string TimeZoneId { get; set; } = "UTC";
    }

    public sealed class RowModel
    {
        public string Id { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "User";
        public string TimeZoneId { get; set; } = "UTC";
        public int Status { get; set; }
    }

    public sealed class ResetModel
    {
        public string UserId { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
