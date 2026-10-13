using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Customers;

/// <summary>Customer editor with nested projects and customer-level tasks grids.</summary>
public sealed class EditModel : PageModel
{
    private readonly ApiClient _api;
    public EditModel(ApiClient api) => _api = api;

    public ListItemResponse? Customer { get; private set; }
    public List<ListItemResponse> Projects { get; private set; } = new();
    public List<ListItemResponse> CustomerTasks { get; private set; } = new();
    public List<TagResponse> CustomerTags { get; private set; } = new();
    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public CustomerForm Input { get; set; } = new();

    [BindProperty]
    public ProjectInput NewProject { get; set; } = new();

    [BindProperty]
    public TaskInput NewTask { get; set; } = new();

    [BindProperty]
    public string NewCustomerTag { get; set; } = "";

    [BindProperty]
    public string NewCustomerTagNotes { get; set; } = "";

    [BindProperty]
    public string EditTag { get; set; } = "";

    [BindProperty]
    public string EditTagNotes { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct))
        {
            Error = string.IsNullOrWhiteSpace(code)
                ? "Missing customer code."
                : $"Customer '{code}' was not found.";
            return Page();
        }
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(string code, CancellationToken ct) =>
        SaveCoreAsync(code, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(string code, CancellationToken ct) =>
        SaveCoreAsync(code, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(string code, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        // Keep posted form values — LoadAsync would otherwise overwrite Input from DB.
        var posted = Input;
        if (!await LoadAsync(code, ct, populateInput: false) || Customer is null)
        {
            Error = $"Customer '{code}' was not found.";
            Input = posted;
            return Page();
        }
        if (Customer.Id == WellKnownIds.None)
        {
            Error = "Cannot modify NONE.";
            Input = posted;
            return Page();
        }
        var (item, error) = await _api.UpdateListItemAsync("customers", Customer.Id, new ListItemRequest
        {
            Code = posted.Code.Trim(),
            Name = posted.Name.Trim(),
            Tags = posted.Tags,
            Notes = posted.Notes,
            Status = posted.Status,
            ColourCode = string.IsNullOrWhiteSpace(posted.ColourCode) ? null : posted.ColourCode.Trim()
        }, ct);
        if (error is not null)
        {
            Error = error;
            Input = posted;
            return Page();
        }

        if (close)
            return RedirectToPage("/Customers/Index");

        Message = "Customer saved.";
        var nextCode = item is not null && !string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)
            ? item.Code
            : code;
        if (item is not null && !string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            return RedirectToPage("/Customers/Edit", new { code = item.Code });
        await LoadAsync(nextCode, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateProjectAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(NewProject.Name))
        {
            Error = "Name is required.";
            return Page();
        }
        var name = NewProject.Name.Trim();
        var allProjects = await _api.ListItemsAsync("projects", ct) ?? new();
        var projCode = string.IsNullOrWhiteSpace(NewProject.Code)
            ? ListCodeGenerator.Allocate(name, allProjects.Select(p => p.Code))
            : NewProject.Code.Trim();
        var (item, error) = await _api.CreateListItemAsync("projects", new ListItemRequest
        {
            Code = projCode,
            Name = name,
            Tags = NewProject.Tags,
            Notes = NewProject.Notes,
            Status = NewProject.Status,
            ColourCode = string.IsNullOrWhiteSpace(NewProject.ColourCode) ? null : NewProject.ColourCode.Trim(),
            CustomerId = Customer.Id
        }, ct);
        if (error is not null) Error = error;
        else Message = $"Created project {item!.Name}.";
        NewProject = new ProjectInput();
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteProjectAsync(string code, Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (id == WellKnownIds.None) Error = "Cannot delete NONE.";
        else
        {
            var error = await _api.DeleteListItemAsync("projects", id, ct);
            if (error is not null)
                Error = error.Contains("409") || error.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
                    ? "Can't delete — tasks still reference it."
                    : error;
            else Message = "Project deleted.";
        }
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateCustomerTagAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(NewCustomerTag))
        {
            Error = "Tag is required.";
            return Page();
        }
        var (_, error) = await _api.CreateTagAsync(new TagRequest
        {
            Tag = NewCustomerTag.Trim(),
            Notes = string.IsNullOrWhiteSpace(NewCustomerTagNotes) ? null : NewCustomerTagNotes.Trim(),
            CustomerId = Customer.Id
        }, ct);
        if (error is not null)
            Error = error.Contains("global", StringComparison.OrdinalIgnoreCase)
                ? "That tag already exists as a general (org-wide) tag — use it from autocomplete instead."
                : FriendlyTagError(error);
        else Message = $"Created customer tag “{NewCustomerTag.Trim()}”.";
        NewCustomerTag = "";
        NewCustomerTagNotes = "";
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveCustomerTagNotesAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(EditTag))
        {
            Error = "Missing tag.";
            return Page();
        }
        var (_, error) = await _api.UpdateTagAsync(new UpdateTagRequest
        {
            Tag = EditTag.Trim(),
            CustomerId = Customer.Id,
            Notes = string.IsNullOrWhiteSpace(EditTagNotes) ? null : EditTagNotes.Trim()
        }, ct);
        if (error is not null) Error = FriendlyTagError(error);
        else Message = $"Saved notes for “{EditTag.Trim()}”.";
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteCustomerTagAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(EditTag))
        {
            Error = "Missing tag.";
            return Page();
        }
        var error = await _api.DeleteTagAsync(EditTag.Trim(), Customer.Id, ct);
        if (error is not null) Error = FriendlyTagError(error);
        else Message = $"Deleted “{EditTag.Trim()}”.";
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostMakeCustomerTagGlobalAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(EditTag))
        {
            Error = "Missing tag.";
            return Page();
        }
        var (_, error) = await _api.MakeTagGlobalAsync(EditTag.Trim(), Customer.Id, ct);
        if (error is not null) Error = FriendlyTagError(error);
        else Message = $"Moved “{EditTag.Trim()}” to the general tag list.";
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateTaskAsync(string code, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, ct) || Customer is null) return NotFound();
        if (string.IsNullOrWhiteSpace(NewTask.Name))
        {
            Error = "Name is required.";
            return Page();
        }
        var name = NewTask.Name.Trim();
        var allTasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        var taskCode = string.IsNullOrWhiteSpace(NewTask.Code)
            ? ListCodeGenerator.Allocate(name, allTasks.Select(t => t.Code))
            : NewTask.Code.Trim();
        var (item, error) = await _api.CreateListItemAsync("tasks", new ListItemRequest
        {
            Code = taskCode,
            Name = name,
            Tags = NewTask.Tags,
            Notes = NewTask.Notes,
            Status = NewTask.Status,
            ColourCode = string.IsNullOrWhiteSpace(NewTask.ColourCode) ? null : NewTask.ColourCode.Trim(),
            CustomerId = Customer.Id
        }, ct);
        if (error is not null) Error = error;
        else Message = $"Created task {item!.Name}.";
        NewTask = new TaskInput();
        await LoadAsync(code, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteTaskAsync(string code, Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (id == WellKnownIds.None) Error = "Cannot delete NONE.";
        else
        {
            var error = await _api.DeleteListItemAsync("tasks", id, ct);
            if (error is not null) Error = error;
            else Message = "Task deleted.";
        }
        await LoadAsync(code, ct);
        return Page();
    }

    private async Task<bool> LoadAsync(string code, CancellationToken ct, bool populateInput = true)
    {
        var me = await _api.GetMeAsync(ct);
        IsAdmin = me?.Role == "Admin";
        var customers = await _api.ListItemsAsync("customers", ct) ?? new();
        Customer = customers.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        if (Customer is null) return false;

        if (populateInput)
        {
            Input = new CustomerForm
            {
                Code = Customer.Code,
                Name = Customer.Name,
                Tags = Customer.Tags ?? "",
                Notes = Customer.Notes ?? "",
                Status = Customer.Status,
                ColourCode = Customer.ColourCode ?? ""
            };
        }

        CustomerTags = (await _api.ListTagsAsync(Customer.Id, scope: "customer", ct) ?? new())
            .OrderBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = await _api.ListItemsAsync("projects", ct) ?? new();
        ProjectCodesJson = System.Text.Json.JsonSerializer.Serialize(projects.Select(p => p.Code).ToList());
        Projects = projects.Where(p => p.CustomerId == Customer.Id)
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase).ToList();

        var tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        TaskCodesJson = System.Text.Json.JsonSerializer.Serialize(tasks.Select(t => t.Code).ToList());
        CustomerTasks = tasks
            .Where(t => t.CustomerId == Customer.Id && (t.ProjectId is null || t.ProjectId == WellKnownIds.None || t.ProjectId == Uuid7.Empty))
            .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase).ToList();
        return true;
    }

    private static string FriendlyTagError(string error) =>
        error.Length > 200 ? error[..200] + "…" : error;

    public sealed class CustomerForm
    {
        [Required] public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }

    public sealed class ProjectInput
    {
        public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }

    public sealed class TaskInput
    {
        public string Code { get; set; } = "";
        [Required] public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Notes { get; set; } = "";
        public int Status { get; set; }
        public string ColourCode { get; set; } = "";
    }

    /// <summary>All project codes (table-unique) for create-dialog suggestions.</summary>
    public string ProjectCodesJson { get; private set; } = "[]";
    /// <summary>All task codes (table-unique) for create-dialog suggestions.</summary>
    public string TaskCodesJson { get; private set; } = "[]";
}
