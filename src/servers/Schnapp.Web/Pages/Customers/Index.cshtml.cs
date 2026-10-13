using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Customers;

/// <summary>Customers grid with create/delete; edit opens dedicated page.</summary>
public sealed class IndexModel : PageModel
{
    private readonly ApiClient _api;
    public IndexModel(ApiClient api) => _api = api;

    public List<ListItemResponse> Customers { get; private set; } = new();
    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public CustomerInput NewCustomer { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(NewCustomer.Name))
        {
            Error = "Name is required.";
            await LoadAsync(ct);
            return Page();
        }
        var existing = await _api.ListItemsAsync("customers", ct) ?? new();
        var name = NewCustomer.Name.Trim();
        var code = string.IsNullOrWhiteSpace(NewCustomer.Code)
            ? ListCodeGenerator.Allocate(name, existing.Select(c => c.Code))
            : NewCustomer.Code.Trim();
        var (item, error) = await _api.CreateListItemAsync("customers", new ListItemRequest
        {
            Code = code,
            Name = name,
            Notes = NewCustomer.Notes,
            Tags = NewCustomer.Tags,
            Status = NewCustomer.Status,
            ColourCode = string.IsNullOrWhiteSpace(NewCustomer.ColourCode) ? null : NewCustomer.ColourCode.Trim()
        }, ct);
        if (error is not null) Error = Friendly(error);
        else Message = $"Created {item!.Name}.";
        NewCustomer = new CustomerInput();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (id == WellKnownIds.None) Error = "Cannot delete NONE.";
        else
        {
            var error = await _api.DeleteListItemAsync("customers", id, ct);
            if (error is not null) Error = Friendly(error);
            else Message = "Deleted.";
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var me = await _api.GetMeAsync(ct);
        IsAdmin = me?.Role == "Admin";
        Customers = (await _api.ListItemsAsync("customers", ct) ?? new())
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Friendly(string error) =>
        error.Contains("409") || error.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
            ? "Can't delete — projects or tasks still reference it. Remove those first."
            : error;

    /// <summary>Existing codes for client-side code suggestion.</summary>
    public string ExistingCodesJson =>
        System.Text.Json.JsonSerializer.Serialize(Customers.Select(c => c.Code).ToList());

    public sealed class CustomerInput
    {
        public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }
}
