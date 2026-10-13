using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;

namespace Schnapp.Web.Pages.Tags;

/// <summary>Org-global tag dictionary editor (tags with no customer id).</summary>
public sealed class IndexModel : PageModel
{
    private readonly ApiClient _api;
    public IndexModel(ApiClient api) => _api = api;

    public List<TagResponse> Tags { get; private set; } = new();
    public List<ListItemResponse> Customers { get; private set; } = new();
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public string NewTag { get; set; } = "";

    [BindProperty]
    public string NewNotes { get; set; } = "";

    [BindProperty]
    public string EditTag { get; set; } = "";

    [BindProperty]
    public string EditNotes { get; set; } = "";

    [BindProperty]
    public string AssignTag { get; set; } = "";

    [BindProperty]
    public string AssignCustomerId { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(NewTag))
        {
            Error = "Tag is required.";
            await LoadAsync(ct);
            return Page();
        }
        var (_, error) = await _api.CreateTagAsync(new TagRequest
        {
            Tag = NewTag.Trim(),
            Notes = string.IsNullOrWhiteSpace(NewNotes) ? null : NewNotes.Trim()
        }, ct);
        if (error is not null) Error = Friendly(error);
        else Message = $"Created tag “{NewTag.Trim()}”.";
        NewTag = "";
        NewNotes = "";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveNotesAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(EditTag))
        {
            Error = "Missing tag.";
            await LoadAsync(ct);
            return Page();
        }
        var (_, error) = await _api.UpdateTagAsync(new UpdateTagRequest
        {
            Tag = EditTag.Trim(),
            CustomerId = null,
            Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes.Trim()
        }, ct);
        if (error is not null) Error = Friendly(error);
        else Message = $"Saved notes for “{EditTag.Trim()}”.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(EditTag))
        {
            Error = "Missing tag.";
            await LoadAsync(ct);
            return Page();
        }
        var error = await _api.DeleteTagAsync(EditTag.Trim(), customerId: null, ct);
        if (error is not null) Error = Friendly(error);
        else Message = $"Deleted “{EditTag.Trim()}”.";
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (string.IsNullOrWhiteSpace(AssignTag))
        {
            Error = "Missing tag.";
            await LoadAsync(ct);
            return Page();
        }
        if (!Uuid7Text.TryParse(AssignCustomerId, out var customerId) || customerId == Uuid7.Empty
            || customerId == WellKnownIds.None)
        {
            Error = "Select a customer.";
            await LoadAsync(ct);
            return Page();
        }
        var (tag, error) = await _api.AssignTagToCustomerAsync(AssignTag.Trim(), customerId, ct);
        if (error is not null) Error = Friendly(error);
        else
        {
            var custName = Customers.FirstOrDefault(c => c.Id == customerId)?.Name
                ?? (await _api.ListItemsAsync("customers", ct))?.FirstOrDefault(c => c.Id == customerId)?.Name
                ?? "customer";
            Message = $"Assigned “{AssignTag.Trim()}” to {custName}.";
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Tags = (await _api.ListTagsAsync(customerId: null, scope: "global", ct) ?? new())
            .OrderBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Customers = (await _api.ListItemsAsync("customers", ct) ?? new())
            .Where(c => c.Id != WellKnownIds.None)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Friendly(string error)
    {
        if (error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            return "That tag already exists.";
        if (error.Contains("global tag", StringComparison.OrdinalIgnoreCase))
            return "That tag already exists as a global tag.";
        return error.Length > 200 ? error[..200] + "…" : error;
    }
}
