using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

namespace Schnapp.Web.Pages.Account;

/// <summary>
/// Edit signed-in user profile fields via <c>PATCH /api/v1/me</c>.
/// Machines are listed read-only (register under Admin · Machines).
/// </summary>
public sealed class ProfileModel : PageModel
{
    private readonly ApiClient _api;

    public ProfileModel(ApiClient api) => _api = api;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<MachineResponse> Machines { get; private set; } = new();
    public List<MachineResponse> FilteredMachines { get; private set; } = new();

    [BindProperty(SupportsGet = true, Name = "machineStatus")]
    public int? MachineStatusFilter { get; set; } = MachineStatus.Active;

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string Email { get; private set; } = "";
    public string UserId { get; private set; } = "";

    public List<string> TimeZoneOptions { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        var me = await _api.GetMeAsync(ct);
        if (me is null)
        {
            AuthCookie.Clear(HttpContext);
            return RedirectToPage("/Login");
        }

        ApplyProfile(me);
        await LoadMachinesAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        if (!ModelState.IsValid)
        {
            Error = "Some fields are invalid: " + string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            await LoadMachinesAsync(ct);
            return Page();
        }

        try
        {
            var me = await _api.GetMeAsync(ct);
            if (me is null)
            {
                AuthCookie.Clear(HttpContext);
                return RedirectToPage("/Login");
            }
            Email = me.Email;

            var (updated, error) = await _api.UpdateMeAsync(new UpdateProfileRequest
            {
                Name = Input.Name,
                TimeZoneId = Input.TimeZoneId,
                Tags = Input.Tags,
                Notes = Input.Notes,
                Status = Input.Status,
                ColourCode = string.IsNullOrWhiteSpace(Input.ColourCode) ? null : Input.ColourCode.Trim()
            }, ct);

            if (updated is null && error is null)
            {
                AuthCookie.Clear(HttpContext);
                return RedirectToPage("/Login");
            }

            if (updated is null)
            {
                Error = error;
                ApplyProfile(me);
                await LoadMachinesAsync(ct);
                return Page();
            }

            Message = "Profile saved.";
            ApplyProfile(updated);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        await LoadMachinesAsync(ct);
        return Page();
    }

    private void ApplyProfile(UserProfileResponse me)
    {
        Email = me.Email;
        UserId = Uuid7Text.Format(me.Id);
        Input = new InputModel
        {
            Code = me.Code,
            Name = me.Name,
            TimeZoneId = me.TimeZoneId,
            Tags = me.Tags ?? "",
            Notes = me.Notes ?? "",
            Status = me.Status,
            ColourCode = me.ColourCode ?? ""
        };
    }

    private async Task LoadMachinesAsync(CancellationToken ct)
    {
        Machines = await _api.ListMachinesAsync(ct) ?? new();
        FilteredMachines = MachineStatusFilter is null
            ? Machines
            : Machines.Where(m => m.Status == MachineStatusFilter.Value).ToList();
    }

    public sealed class InputModel
    {
        [Required]
        public string Code { get; set; } = "";

        [Required]
        public string Name { get; set; } = "";

        [Required]
        public string TimeZoneId { get; set; } = "UTC";

        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }
}
