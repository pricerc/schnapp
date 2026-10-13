using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Schnapp.Contracts;
using Schnapp.Domain;
using Schnapp.Web.Services;

using Medo;
namespace Schnapp.Web.Pages.Customers;

/// <summary>Project editor with nested project tasks.</summary>
public sealed class ProjectModel : PageModel
{
    private readonly ApiClient _api;
    public ProjectModel(ApiClient api) => _api = api;

    public ListItemResponse? Customer { get; private set; }
    public ListItemResponse? Project { get; private set; }
    public List<ListItemResponse> Tasks { get; private set; } = new();
    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public ProjectForm Input { get; set; } = new();

    [BindProperty]
    public TaskInput NewTask { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string code, string projectCode, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, projectCode, ct)) return NotFound();
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(string code, string projectCode, CancellationToken ct) =>
        SaveCoreAsync(code, projectCode, close: false, ct);

    public Task<IActionResult> OnPostSaveAndCloseAsync(string code, string projectCode, CancellationToken ct) =>
        SaveCoreAsync(code, projectCode, close: true, ct);

    private async Task<IActionResult> SaveCoreAsync(string code, string projectCode, bool close, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        // Keep posted form values — LoadAsync would otherwise overwrite Input from DB.
        var posted = Input;
        if (!await LoadAsync(code, projectCode, ct, populateInput: false) || Project is null || Customer is null)
        {
            Error = "Project not found.";
            Input = posted;
            return Page();
        }
        if (Project.Id == WellKnownIds.None)
        {
            Error = "Cannot modify NONE.";
            Input = posted;
            return Page();
        }
        var customerCode = Customer.Code;
        var (item, error) = await _api.UpdateListItemAsync("projects", Project.Id, new ListItemRequest
        {
            Code = posted.Code.Trim(),
            Name = posted.Name.Trim(),
            Tags = posted.Tags,
            Notes = posted.Notes,
            Status = posted.Status,
            ColourCode = string.IsNullOrWhiteSpace(posted.ColourCode) ? null : posted.ColourCode.Trim(),
            CustomerId = Customer.Id
        }, ct);
        if (error is not null)
        {
            Error = error;
            Input = posted;
            return Page();
        }

        if (close)
            return Redirect($"/Customers/{Uri.EscapeDataString(customerCode)}");

        Message = "Project saved.";
        if (item is not null && !string.Equals(item.Code, projectCode, StringComparison.OrdinalIgnoreCase))
            return RedirectToPage("/Customers/Project", new { code = customerCode, projectCode = item.Code });
        await LoadAsync(code, item?.Code ?? projectCode, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateTaskAsync(string code, string projectCode, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (!await LoadAsync(code, projectCode, ct) || Project is null || Customer is null) return NotFound();
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
            CustomerId = Customer.Id,
            ProjectId = Project.Id
        }, ct);
        if (error is not null) Error = error;
        else Message = $"Created task {item!.Name}.";
        NewTask = new TaskInput();
        await LoadAsync(code, projectCode, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteTaskAsync(string code, string projectCode, Uuid7Param id, CancellationToken ct)
    {
        if (AuthCookie.GetToken(HttpContext) is null) return RedirectToPage("/Login");
        if (id == WellKnownIds.None) Error = "Cannot delete NONE.";
        else
        {
            var error = await _api.DeleteListItemAsync("tasks", id, ct);
            if (error is not null) Error = error;
            else Message = "Task deleted.";
        }
        await LoadAsync(code, projectCode, ct);
        return Page();
    }

    private async Task<bool> LoadAsync(string code, string projectCode, CancellationToken ct, bool populateInput = true)
    {
        var me = await _api.GetMeAsync(ct);
        IsAdmin = me?.Role == "Admin";
        var customers = await _api.ListItemsAsync("customers", ct) ?? new();
        Customer = customers.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        if (Customer is null) return false;

        var projects = await _api.ListItemsAsync("projects", ct) ?? new();
        Project = projects.FirstOrDefault(p =>
            p.CustomerId == Customer.Id
            && string.Equals(p.Code, projectCode, StringComparison.OrdinalIgnoreCase));
        if (Project is null) return false;

        if (populateInput)
        {
            Input = new ProjectForm
            {
                Code = Project.Code,
                Name = Project.Name,
                Tags = Project.Tags ?? "",
                Notes = Project.Notes ?? "",
                Status = Project.Status,
                ColourCode = Project.ColourCode ?? ""
            };
        }

        var tasks = await _api.ListItemsAsync("tasks", ct) ?? new();
        TaskCodesJson = System.Text.Json.JsonSerializer.Serialize(tasks.Select(t => t.Code).ToList());
        Tasks = tasks.Where(t => t.ProjectId == Project.Id)
            .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase).ToList();
        return true;
    }

    public string TaskCodesJson { get; private set; } = "[]";

    public sealed class ProjectForm
    {
        [Required] public string Code { get; set; } = "";
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
}
