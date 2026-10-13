using Schnapp.Api.Auth;
using Schnapp.Api.Tags;
using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Api.Lists;

/// <summary>
/// Org-global list CRUD (customers, projects, tasks, journal targets, tags, entity statuses)
/// and key/value settings under <c>/api/v1</c>.
/// </summary>
/// <remarks>
/// Called from <c>Program.cs</c> via <see cref="MapListEndpoints"/>. Authenticated users can
/// read and mutate lists; only admins can patch settings values.
/// Free-text tags on list entities are normalized and collated into the tag dictionary.
/// </remarks>
public static class ListEndpoints
{
    /// <summary>
    /// Registers list and settings routes under <c>/api/v1</c>.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapListEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireAuthFilter>();

        api.MapGet("/lists/customers", async (ListRepository lists, CancellationToken ct) =>
            Results.Ok((await lists.ListCustomersAsync(ct)).Select(ToCustomer)));
        api.MapPost("/lists/customers", CreateCustomerAsync);
        api.MapPut("/lists/customers/{id}", UpdateCustomerAsync);
        api.MapDelete("/lists/customers/{id}", DeleteCustomerAsync);

        api.MapGet("/lists/projects", async (ListRepository lists, CancellationToken ct) =>
            Results.Ok((await lists.ListProjectsAsync(ct)).Select(ToProject)));
        api.MapPost("/lists/projects", CreateProjectAsync);
        api.MapPut("/lists/projects/{id}", UpdateProjectAsync);
        api.MapDelete("/lists/projects/{id}", DeleteProjectAsync);

        api.MapGet("/lists/tasks", async (ListRepository lists, CancellationToken ct) =>
            Results.Ok((await lists.ListTasksAsync(ct)).Select(ToTask)));
        api.MapPost("/lists/tasks", CreateTaskAsync);
        api.MapPut("/lists/tasks/{id}", UpdateTaskAsync);
        api.MapDelete("/lists/tasks/{id}", DeleteTaskAsync);

        api.MapGet("/lists/journal-targets", async (ListRepository lists, CancellationToken ct) =>
            Results.Ok((await lists.ListTargetsAsync(ct)).Select(ToTarget)));
        api.MapPost("/lists/journal-targets", GetOrCreateTargetAsync);
        api.MapPatch("/lists/journal-targets/{id}", UpdateTargetAsync);

        api.MapGet("/lists/tags", ListTagsAsync);
        api.MapPost("/lists/tags", CreateTagAsync);
        api.MapPatch("/lists/tags", UpdateTagAsync);
        api.MapDelete("/lists/tags", DeleteTagAsync);
        api.MapPost("/lists/tags/assign-customer", AssignTagToCustomerAsync);
        api.MapPost("/lists/tags/make-global", MakeTagGlobalAsync);

        api.MapGet("/lists/entity-statuses", ListEntityStatusesAsync);

        api.MapGet("/settings", async (SettingRepository settings, CancellationToken ct) =>
            Results.Ok((await settings.ListAsync(ct)).Select(ToSetting)));
        api.MapGet("/settings/{key}", async (string key, SettingRepository settings, CancellationToken ct) =>
        {
            var s = await settings.GetAsync(key, ct);
            return s is null ? Results.NotFound() : Results.Ok(ToSetting(s));
        });

        var admin = app.MapGroup("/api/v1").AddEndpointFilter<RequireAdminFilter>();
        admin.MapPatch("/settings/{key}", UpdateSettingAsync);

        return app;
    }

    /// <summary>
    /// Lists dictionary tags.
    /// </summary>
    /// <param name="customerId">Optional customer filter (Id26/RFC).</param>
    /// <param name="scope">
    /// <c>global</c> — only org-global tags;
    /// <c>customer</c> — only that customer's tags (requires customerId);
    /// omitted — all tags, or global+customer when customerId is set (autocomplete).
    /// </param>
    private static async Task<IResult> ListTagsAsync(
        string? customerId, string? scope, ListRepository lists, CancellationToken ct)
    {
        Uuid7? cid = null;
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            if (!Uuid7Text.TryParse(customerId, out var parsed))
                return Results.BadRequest(new { error = "Invalid customerId." });
            cid = parsed;
        }

        ListRepository.TagListScope listScope = ListRepository.TagListScope.Default;
        if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase))
            listScope = ListRepository.TagListScope.GlobalOnly;
        else if (string.Equals(scope, "customer", StringComparison.OrdinalIgnoreCase))
        {
            if (cid is null || cid == WellKnownIds.None)
                return Results.BadRequest(new { error = "customerId is required when scope=customer." });
            listScope = ListRepository.TagListScope.CustomerOnly;
        }

        try
        {
            var rows = await lists.ListTagsAsync(cid, listScope, ct);
            return Results.Ok(rows.Select(ToTag));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CreateCustomerAsync(ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        if (body.Id == WellKnownIds.None) return Results.BadRequest(new { error = "Nil id is reserved." });
        var now = DateTimeOffset.UtcNow;
        var id = body.Id ?? IdFactory.NewId();
        // Normalize first; collate into dictionary after insert (FK → customer).
        var tags = TagText.Normalize(body.Tags);
        var c = new Customer
        {
            Id = id,
            Code = body.Code.Trim(),
            Name = body.Name.Trim(),
            Notes = body.Notes,
            Tags = EmptyToNull(tags),
            Status = body.Status,
            ColourCode = body.ColourCode,
            CreatedAt = now,
            UpdatedAt = now
        };
        try { await lists.InsertCustomerAsync(c, ct); }
        catch { return Results.Conflict(new { error = "Code or name already exists." }); }
        // Customer tags are scoped to that customer so they surface in C/P/T autocomplete.
        await lists.EnsureTagsAsync(tags, id, ct);
        return Results.Created($"/api/v1/lists/customers/{Uuid7Text.Format(c.Id)}", ToCustomer(c));
    }

    private static async Task<IResult> UpdateCustomerAsync(Uuid7Param id, ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot modify NONE sentinel." });
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        var existing = await lists.GetCustomerAsync(id, ct);
        if (existing is null) return Results.NotFound();
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, id, ct);
        existing.Code = body.Code.Trim();
        existing.Name = body.Name.Trim();
        existing.Notes = body.Notes;
        existing.Tags = EmptyToNull(tags);
        existing.Status = body.Status;
        existing.ColourCode = body.ColourCode;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        try { await lists.UpdateCustomerAsync(existing, ct); }
        catch { return Results.Conflict(new { error = "Code or name already exists." }); }
        return Results.Ok(ToCustomer(existing));
    }

    private static async Task<IResult> DeleteCustomerAsync(Uuid7Param id, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot delete NONE sentinel." });
        return await lists.DeleteCustomerAsync(id, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> CreateProjectAsync(ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        if (body.Id == WellKnownIds.None) return Results.BadRequest(new { error = "Nil id is reserved." });
        var now = DateTimeOffset.UtcNow;
        var customerId = body.CustomerId ?? WellKnownIds.None;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, customerId, ct);
        var p = new Project
        {
            Id = body.Id ?? IdFactory.NewId(),
            Code = body.Code.Trim(),
            Name = body.Name.Trim(),
            CustomerId = customerId,
            Notes = body.Notes,
            Tags = EmptyToNull(tags),
            Status = body.Status,
            ColourCode = body.ColourCode,
            CreatedAt = now,
            UpdatedAt = now
        };
        try { await lists.InsertProjectAsync(p, ct); }
        catch { return Results.Conflict(new { error = "Code or name already exists (or invalid customer)." }); }
        return Results.Created($"/api/v1/lists/projects/{Uuid7Text.Format(p.Id)}", ToProject(p));
    }

    private static async Task<IResult> UpdateProjectAsync(Uuid7Param id, ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot modify NONE sentinel." });
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        var customerId = body.CustomerId ?? WellKnownIds.None;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, customerId, ct);
        var p = new Project
        {
            Id = id,
            Code = body.Code.Trim(),
            Name = body.Name.Trim(),
            CustomerId = customerId,
            Notes = body.Notes,
            Tags = EmptyToNull(tags),
            Status = body.Status,
            ColourCode = body.ColourCode,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try { await lists.UpdateProjectAsync(p, ct); }
        catch { return Results.Conflict(new { error = "Update failed." }); }
        return Results.Ok(ToProject(p));
    }

    private static async Task<IResult> DeleteProjectAsync(Uuid7Param id, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot delete NONE sentinel." });
        return await lists.DeleteProjectAsync(id, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> CreateTaskAsync(ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        if (body.Id == WellKnownIds.None) return Results.BadRequest(new { error = "Nil id is reserved." });
        var now = DateTimeOffset.UtcNow;
        var customerId = body.CustomerId ?? WellKnownIds.None;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, customerId, ct);
        var t = new TaskItem
        {
            Id = body.Id ?? IdFactory.NewId(),
            Code = body.Code.Trim(),
            Name = body.Name.Trim(),
            CustomerId = customerId,
            ProjectId = body.ProjectId ?? WellKnownIds.None,
            Notes = body.Notes,
            Tags = EmptyToNull(tags),
            Status = body.Status,
            ColourCode = body.ColourCode,
            CreatedAt = now,
            UpdatedAt = now
        };
        try { await lists.InsertTaskAsync(t, ct); }
        catch { return Results.Conflict(new { error = "Code or name already exists (or invalid parent)." }); }
        return Results.Created($"/api/v1/lists/tasks/{Uuid7Text.Format(t.Id)}", ToTask(t));
    }

    private static async Task<IResult> UpdateTaskAsync(Uuid7Param id, ListItemRequest body, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot modify NONE sentinel." });
        if (!ValidCodeName(body, out var err)) return Results.BadRequest(new { error = err });
        var customerId = body.CustomerId ?? WellKnownIds.None;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, customerId, ct);
        var t = new TaskItem
        {
            Id = id,
            Code = body.Code.Trim(),
            Name = body.Name.Trim(),
            CustomerId = customerId,
            ProjectId = body.ProjectId ?? WellKnownIds.None,
            Notes = body.Notes,
            Tags = EmptyToNull(tags),
            Status = body.Status,
            ColourCode = body.ColourCode,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try { await lists.UpdateTaskAsync(t, ct); }
        catch { return Results.Conflict(new { error = "Update failed." }); }
        return Results.Ok(ToTask(t));
    }

    private static async Task<IResult> ListEntityStatusesAsync(
        string? entity, ListRepository lists, CancellationToken ct)
    {
        var rows = await lists.ListEntityStatusesAsync(entity, ct);
        return Results.Ok(rows.Select(s => new EntityStatusResponse
        {
            Entity = s.Entity,
            Status = s.Status,
            Description = s.Description,
            ColourCode = s.ColourCode
        }));
    }

    private static async Task<IResult> DeleteTaskAsync(Uuid7Param id, ListRepository lists, CancellationToken ct)
    {
        if (id == WellKnownIds.None) return Results.BadRequest(new { error = "Cannot delete NONE sentinel." });
        return await lists.DeleteTaskAsync(id, ct) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetOrCreateTargetAsync(
        JournalTargetRequest body, ListRepository lists, IDatabaseDialect dialect, CancellationToken ct)
    {
        try
        {
            var target = await lists.GetOrCreateTargetAsync(
                body.CustomerId, body.ProjectId, body.TaskId, body.Code, body.Name, body.Id, ct);
            if (body.Notes is not null || body.Tags is not null || body.Code is not null || body.Name is not null)
            {
                string? tags = target.Tags;
                if (body.Tags is not null)
                {
                    tags = await TagCollation.NormalizeAndEnsureRequiredAsync(
                        lists, body.Tags,
                        body.CustomerId != Uuid7.Empty ? body.CustomerId : target.CustomerId, ct);
                    tags = EmptyToNull(tags);
                }
                target = await lists.UpdateTargetAsync(
                    target.Id,
                    body.Code ?? target.Code,
                    body.Name ?? target.Name,
                    body.Notes ?? target.Notes,
                    tags,
                    ct) ?? target;
            }
            return Results.Ok(ToTarget(target));
        }
        catch (Exception ex) when (dialect.IsForeignKeyViolation(ex))
        {
            // Offline clients may send C/P/T ids not yet flushed — tell them to push list creates first.
            return Results.BadRequest(new
            {
                error = "Customer, project, or task id does not exist yet. Create those rows first (or push offline list mutations before journal-targets).",
                customerId = body.CustomerId,
                projectId = body.ProjectId,
                taskId = body.TaskId
            });
        }
    }

    private static async Task<IResult> UpdateTargetAsync(
        Uuid7Param id, UpdateJournalTargetRequest body, ListRepository lists, CancellationToken ct)
    {
        var existing = await lists.GetTargetAsync(id, ct);
        if (existing is null) return Results.NotFound();

        string? tags = body.Tags;
        if (tags is not null)
        {
            tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, tags, existing.CustomerId, ct);
            tags = EmptyToNull(tags);
        }

        var target = await lists.UpdateTargetAsync(id, body.Code, body.Name, body.Notes, tags, ct);
        return target is null ? Results.NotFound() : Results.Ok(ToTarget(target));
    }



    private static async Task<IResult> CreateTagAsync(TagRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!TryNormalizeSingleTag(body.Tag, out var normalized, out var err))
            return Results.BadRequest(new { error = err });
        try
        {
            await lists.InsertTagAsync(normalized, body.Notes, body.CustomerId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch
        {
            return Results.Conflict(new { error = "Tag already exists." });
        }

        var cid = body.CustomerId is { } c && c != WellKnownIds.None ? c : (Uuid7?)null;
        return Results.Created($"/api/v1/lists/tags/{Uri.EscapeDataString(normalized)}",
            new TagResponse { Tag = normalized, Notes = body.Notes, CustomerId = cid });
    }

    private static async Task<IResult> UpdateTagAsync(UpdateTagRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!TryNormalizeSingleTag(body.Tag, out var normalized, out var err))
            return Results.BadRequest(new { error = err });
        var ok = await lists.UpdateTagNotesAsync(normalized, body.CustomerId, body.Notes, ct);
        if (!ok) return Results.NotFound(new { error = "Tag not found." });
        var cid = body.CustomerId is { } c && c != WellKnownIds.None ? c : (Uuid7?)null;
        return Results.Ok(new TagResponse { Tag = normalized, Notes = body.Notes, CustomerId = cid });
    }

    private static async Task<IResult> DeleteTagAsync(
        string? tag, string? customerId, ListRepository lists, CancellationToken ct)
    {
        if (!TryNormalizeSingleTag(tag, out var normalized, out var err))
            return Results.BadRequest(new { error = err });
        Uuid7? cid = null;
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            if (!Uuid7Text.TryParse(customerId, out var parsed))
                return Results.BadRequest(new { error = "Invalid customerId." });
            cid = parsed;
        }
        var ok = await lists.DeleteTagAsync(normalized, cid, ct);
        return ok ? Results.NoContent() : Results.NotFound(new { error = "Tag not found." });
    }

    private static async Task<IResult> AssignTagToCustomerAsync(
        ReassignTagRequest body, ListRepository lists, IDatabaseDialect dialect, CancellationToken ct)
    {
        if (!TryNormalizeSingleTag(body.Tag, out var normalized, out var err))
            return Results.BadRequest(new { error = err });
        if (body.CustomerId == Uuid7.Empty || body.CustomerId == WellKnownIds.None)
            return Results.BadRequest(new { error = "customerId is required." });
        try
        {
            var row = await lists.AssignTagToCustomerAsync(normalized, body.CustomerId, ct);
            if (row is null)
                return Results.NotFound(new { error = "Global tag not found." });
            return Results.Ok(ToTag(row));
        }
        catch (Exception ex) when (dialect.IsForeignKeyViolation(ex))
        {
            return Results.BadRequest(new { error = "Unknown customerId." });
        }
    }

    private static async Task<IResult> MakeTagGlobalAsync(
        ReassignTagRequest body, ListRepository lists, CancellationToken ct)
    {
        if (!TryNormalizeSingleTag(body.Tag, out var normalized, out var err))
            return Results.BadRequest(new { error = err });
        if (body.CustomerId == Uuid7.Empty || body.CustomerId == WellKnownIds.None)
            return Results.BadRequest(new { error = "customerId is required." });
        var row = await lists.MakeTagGlobalAsync(normalized, body.CustomerId, ct);
        if (row is null)
            return Results.NotFound(new { error = "Customer tag not found." });
        return Results.Ok(ToTag(row));
    }

    private static bool TryNormalizeSingleTag(string? input, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Tag is required.";
            return false;
        }
        normalized = TagText.Normalize(input);
        if (string.IsNullOrEmpty(normalized) || normalized.Contains(' ', StringComparison.Ordinal))
        {
            error = "Tag must be a single non-empty token.";
            return false;
        }
        return true;
    }

    private static async Task<IResult> UpdateSettingAsync(
        string key, UpdateSettingRequest body, SettingRepository settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Value))
            return Results.BadRequest(new { error = "Value is required." });
        await settings.UpsertAsync(key, body.Value, body.Notes, ct);
        var s = await settings.GetAsync(key, ct);
        return Results.Ok(ToSetting(s!));
    }

    private static bool ValidCodeName(ListItemRequest body, out string? error)
    {
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Name))
        {
            error = "Code and Name are required.";
            return false;
        }
        error = null;
        return true;
    }

    private static string? EmptyToNull(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static TagResponse ToTag(Tag t) => new()
    {
        Tag = t.TagValue,
        Notes = t.Notes,
        CustomerId = t.CustomerId
    };

    private static ListItemResponse ToCustomer(Customer c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        Notes = c.Notes,
        Tags = c.Tags,
        Status = c.Status,
        ColourCode = c.ColourCode,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static ListItemResponse ToProject(Project p) => new()
    {
        Id = p.Id,
        Code = p.Code,
        Name = p.Name,
        Notes = p.Notes,
        Tags = p.Tags,
        CustomerId = p.CustomerId,
        Status = p.Status,
        ColourCode = p.ColourCode,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    private static ListItemResponse ToTask(TaskItem t) => new()
    {
        Id = t.Id,
        Code = t.Code,
        Name = t.Name,
        Notes = t.Notes,
        Tags = t.Tags,
        CustomerId = t.CustomerId,
        ProjectId = t.ProjectId,
        Status = t.Status,
        ColourCode = t.ColourCode,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static JournalTargetResponse ToTarget(JournalTarget t) => new()
    {
        Id = t.Id,
        CustomerId = t.CustomerId,
        ProjectId = t.ProjectId,
        TaskId = t.TaskId,
        Code = t.Code,
        Name = t.Name,
        Notes = t.Notes,
        Tags = t.Tags
    };

    private static SettingResponse ToSetting(Setting s) => new()
    {
        Key = s.Key,
        Value = s.Value,
        Notes = s.Notes,
        UpdatedAt = s.UpdatedAt
    };
}
