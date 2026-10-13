using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Customers;

/// <summary>Standalone task editor.</summary>
public sealed class TaskEditModel : PageModel
{
    private readonly ApiClient _api;
    public TaskEditModel(ApiClient api) => _api = api;

    public ListItemResponse? TaskItem { get; private set; }
    public ListItemResponse? Customer { get; private set; }
    public ListItemResponse? Project { get; private set; }
    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string BackUrl { get; private set; } = "/Customers";

    [BindProperty]
    public TaskForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(id, ct)) return NotFound();
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(Uuid7Param id, CancellationToken ct) =>
        SaveCoreAsync(id, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(Uuid7Param id, CancellationToken ct) =>
        SaveCoreAsync(id, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(Uuid7Param id, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        // Keep posted form values — LoadAsync would otherwise overwrite Input from DB.
        var posted = Input;
        if (!await LoadAsync(id, ct, populateInput: false) || TaskItem is null)
        {
            Error = "Task not found.";
            Input = posted;
            return Page();
        }
        if (TaskItem.Id == WellKnownIds.None)
        {
            Error = "Cannot modify NONE.";
            Input = posted;
            return Page();
        }
        var back = BackUrl;
        var (item, error) = await _api.UpdateListItemAsync("tasks", TaskItem.Id, new ListItemRequest
        {
            Code = posted.Code.Trim(),
            Name = posted.Name.Trim(),
            Tags = posted.Tags,
            Notes = posted.Notes,
            Status = posted.Status,
            ColourCode = string.IsNullOrWhiteSpace(posted.ColourCode) ? null : posted.ColourCode.Trim(),
            CustomerId = TaskItem.CustomerId,
            ProjectId = TaskItem.ProjectId
        }, ct);
        if (error is not null)
        {
            Error = error;
            Input = posted;
            return Page();
        }

        if (close)
            return LocalRedirect(back);

        Message = "Task saved.";
        TaskItem = item;
        await LoadAsync(id, ct);
        return Page();
    }

    private async Task<bool> LoadAsync(Uuid7 id, CancellationToken ct, bool populateInput = true)
    {
        var me = await _api.GetMeAsync(ct);
        IsAdmin = me?.Role == "Admin";
        var tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        TaskItem = tasks.FirstOrDefault(t => t.Id == id);
        if (TaskItem is null) return false;

        if (populateInput)
        {
            Input = new TaskForm
            {
                Code = TaskItem.Code,
                Name = TaskItem.Name,
                Tags = TaskItem.Tags ?? "",
                Notes = TaskItem.Notes ?? "",
                Status = TaskItem.Status,
                ColourCode = TaskItem.ColourCode ?? ""
            };
        }

        var customers = await _api.ListItemsAsync("customers", ct) ?? new();
        if (TaskItem.CustomerId is { } cid)
            Customer = customers.FirstOrDefault(c => c.Id == cid);

        if (TaskItem.ProjectId is { } pid && pid != WellKnownIds.None && pid != Uuid7.Empty)
        {
            var projects = await _api.ListItemsAsync("projects", ct) ?? new();
            Project = projects.FirstOrDefault(p => p.Id == pid);
        }

        if (Customer is not null && Project is not null)
            BackUrl = $"/Customers/{Uri.EscapeDataString(Customer.Code)}/Projects/{Uri.EscapeDataString(Project.Code)}";
        else if (Customer is not null)
            BackUrl = $"/Customers/{Uri.EscapeDataString(Customer.Code)}";
        else
            BackUrl = "/Customers";

        return true;
    }

    public sealed class TaskForm
    {
        [Required] public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }
}
