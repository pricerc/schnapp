using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Admin;

/// <summary>
/// Admin-only: org-wide machines, bulk status, and register dialog.
/// </summary>
public sealed class MachinesModel : PageModel
{
    private readonly ApiClient _api;
    public MachinesModel(ApiClient api) => _api = api;

    public List<AdminMachineResponse> Machines { get; private set; } = new();
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public bool IsAdmin { get; private set; }

    /// <summary>null = all, otherwise filter by machine status code. Default Active.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Status { get; set; } = MachineStatus.Active;

    [BindProperty]
    public List<string> SelectedIds { get; set; } = new();

    [BindProperty]
    public int BulkStatus { get; set; } = MachineStatus.Active;

    [BindProperty]
    public RegisterInput Register { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");

        if (!await EnsureAdminAsync(ct))
            return Page();

        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBulkStatusAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");
        if (!await EnsureAdminAsync(ct))
            return Page();

        var ids = SelectedIds
            .Select(s => Uuid7Text.TryParse(s, out var id) ? id : (Uuid7?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            Error = "Select at least one machine.";
            await LoadAsync(ct);
            return Page();
        }

        if (BulkStatus is not (MachineStatus.Active or MachineStatus.Disabled))
        {
            Error = "Invalid status.";
            await LoadAsync(ct);
            return Page();
        }

        var (updated, error) = await _api.BulkUpdateMachineStatusAsync(ids, BulkStatus, ct);
        if (error is not null)
            Error = Friendly(error);
        else
            Message = $"Set {MachineStatus.Label(BulkStatus)} on {updated} machine(s).";

        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRegisterAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null)
            return RedirectToPage("/Login");
        if (!await EnsureAdminAsync(ct))
            return Page();

        if (string.IsNullOrWhiteSpace(Register.Name))
        {
            Error = "Machine name is required.";
            await LoadAsync(ct);
            return Page();
        }

        var (machine, error) = await _api.CreateMachineAsync(new CreateMachineRequest
        {
            Name = Register.Name.Trim(),
            Code = string.IsNullOrWhiteSpace(Register.Code) ? null : Register.Code.Trim(),
            Fqdn = Register.Fqdn,
            HardwareFingerprint = Register.HardwareFingerprint,
            DisplayName = Register.DisplayName
        }, ct);

        if (error is not null)
            Error = Friendly(error);
        else
        {
            Message = $"Registered machine {machine!.Name}.";
            Register = new RegisterInput();
        }

        await LoadAsync(ct);
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
        if (!IsAdmin)
            Error = "You are signed in but not an Admin. This page requires the Admin role.";
        return IsAdmin;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var all = await _api.ListAllMachinesAsync(ct) ?? new();
        var unique = all
            .GroupBy(m => m.Id)
            .Select(g => g.OrderBy(x => x.UserName).First())
            .ToList();
        Machines = Status is null
            ? unique
            : unique.Where(m => m.Status == Status.Value).ToList();
    }

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

    public static string StatusLabel(int status) => MachineStatus.Label(status);

    public sealed class RegisterInput
    {
        [Required]
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Fqdn { get; set; } = "";
        public string HardwareFingerprint { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
