using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Machines;

/// <summary>Edit a machine (owner or admin).</summary>
public sealed class EditModel : PageModel
{
    private readonly ApiClient _api;
    public EditModel(ApiClient api) => _api = api;

    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string MachineId { get; private set; } = "";
    public string? ReturnUrl { get; private set; }
    public DateTimeOffset? LastSeen { get; private set; }
    public DateTimeOffset? RegisteredAt { get; private set; }

    [BindProperty]
    public FormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string id, string? from, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        ReturnUrl = string.IsNullOrWhiteSpace(from) ? "/Account/Profile" : from;
        if (!Uuid7Text.TryParse(id, out var machineId))
        {
            Error = "Invalid machine id.";
            return Page();
        }
        await LoadAsync(machineId, ct);
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(string id, string? from, CancellationToken ct) =>
        SaveCoreAsync(id, from, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(string id, string? from, CancellationToken ct) =>
        SaveCoreAsync(id, from, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(string id, string? from, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        ReturnUrl = string.IsNullOrWhiteSpace(from) ? "/Account/Profile" : from;
        if (!Uuid7Text.TryParse(id, out var machineId))
        {
            Error = "Invalid machine id.";
            return Page();
        }
        MachineId = Uuid7Text.Format(machineId);

        if (!ModelState.IsValid)
        {
            Error = "Check required fields.";
            return Page();
        }

        var (machine, error) = await _api.UpdateMachineAsync(machineId, new UpdateMachineRequest
        {
            Code = Input.Code.Trim(),
            Name = Input.Name.Trim(),
            Fqdn = Input.Fqdn,
            HardwareFingerprint = Input.HardwareFingerprint,
            Notes = Input.Notes,
            Tags = Input.Tags,
            Status = Input.Status,
            ColourCode = string.IsNullOrWhiteSpace(Input.ColourCode) ? null : Input.ColourCode.Trim(),
            DisplayName = Input.DisplayName
        }, ct);

        if (error is not null)
        {
            Error = Friendly(error);
            return Page();
        }

        if (close)
            return LocalRedirect(ReturnUrl);

        Message = "Machine saved.";
        Apply(machine!);
        return Page();
    }

    private async Task LoadAsync(Uuid7 id, CancellationToken ct)
    {
        var (machine, error) = await _api.GetMachineAsync(id, ct);
        if (machine is null)
        {
            // Fall back to own list / admin list
            var mine = await _api.ListMachinesAsync(ct) ?? [];
            machine = mine.FirstOrDefault(m => m.Id == id);
            if (machine is null)
            {
                var all = await _api.ListAllMachinesAsync(ct) ?? [];
                var admin = all.FirstOrDefault(m => m.Id == id);
                if (admin is not null)
                {
                    machine = new MachineResponse
                    {
                        Id = admin.Id,
                        Code = admin.Code,
                        Name = admin.Name,
                        Fqdn = admin.Fqdn,
                        HardwareFingerprint = admin.HardwareFingerprint,
                        LastSeen = admin.LastSeen,
                        Status = admin.Status,
                        CreatedAt = admin.CreatedAt,
                        UpdatedAt = admin.UpdatedAt,
                        Notes = admin.Notes,
                        Tags = admin.Tags
                    };
                }
            }
            if (machine is null)
            {
                Error = Friendly(error ?? "Machine not found.");
                MachineId = Uuid7Text.Format(id);
                return;
            }
        }
        Apply(machine);
    }

    private void Apply(MachineResponse m)
    {
        MachineId = Uuid7Text.Format(m.Id);
        LastSeen = m.LastSeen;
        RegisteredAt = m.RegisteredAt == default ? null : m.RegisteredAt;
        Input = new FormModel
        {
            Code = m.Code,
            Name = m.Name,
            Fqdn = m.Fqdn ?? "",
            HardwareFingerprint = m.HardwareFingerprint ?? "",
            DisplayName = m.DisplayName ?? "",
            Notes = m.Notes ?? "",
            Tags = m.Tags ?? "",
            Status = m.Status,
            ColourCode = m.ColourCode ?? ""
        };
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

    public sealed class FormModel
    {
        [Required] public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Fqdn { get; set; } = "";
        public string HardwareFingerprint { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Tags { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }
}
