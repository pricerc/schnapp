using System.Globalization;
using System.Security.Claims;
using Schnapp.Api.Auth;
using Schnapp.Api.Reports;
using Schnapp.Api.Screenshots;
using Schnapp.Api.Tags;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Api.Journals;

/// <summary>
/// Journal start/stop/list/update with one open entry per (user, machine).
/// </summary>
/// <remarks>
/// <para>
/// Called from <c>Program.cs</c> via <see cref="MapJournalEndpoints"/>. All routes require
/// a valid bearer token. On start, any existing open entry for the same user+machine is auto-closed.
/// List/get/start also run <see cref="JournalDayBoundary"/> so open entries do not span local days
/// (close at midnight in the user profile timezone, clone machine/target/notes/tags for today).
/// </para>
/// <para>
/// Close time uses org setting <see cref="SettingKeys.JournalRecentLastSeenMinutes"/>:
/// if <see cref="Machine.LastSeen"/> is within that many minutes, close at now; otherwise close at LastSeen
/// (clamped not before the open entry's start).
/// </para>
/// </remarks>
public static class JournalEndpoints
{
    /// <summary>
    /// Registers journal routes under <c>/api/v1</c>.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireAuthFilter>();
        api.MapGet("/journals", ListAsync);
        api.MapGet("/journals/{id}", GetAsync);
        api.MapPost("/journals/start", StartAsync);
        api.MapPost("/journals/{id}/stop", StopAsync);
        api.MapPatch("/journals/{id}", UpdateAsync);
        api.MapDelete("/journals/{id}", DeleteAsync);
        return app;
    }

    /// <summary>
    /// Lists journal entries for the authenticated user (newest first).
    /// Optional <paramref name="fromDate"/> / <paramref name="toDate"/> (local yyyy-MM-dd) and
    /// C/P/T include/exclude filters are applied in SQL. Omitting all filters keeps the
    /// legacy unfiltered recent list (limit 200) for tray/CLI clients.
    /// </summary>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        JournalRepository journals,
        UserRepository users,
        CancellationToken ct,
        string? fromDate = null,
        string? toDate = null,
        int? limit = null,
        bool includeCustomer = false,
        string? includeCustomerValue = null,
        bool includeProject = false,
        string? includeProjectValue = null,
        bool includeTask = false,
        string? includeTaskValue = null,
        bool includeMachine = false,
        string? includeMachineValue = null,
        bool excludeCustomer = false,
        string? excludeCustomerValue = null,
        bool excludeProject = false,
        string? excludeProjectValue = null,
        bool excludeTask = false,
        string? excludeTaskValue = null,
        bool excludeMachine = false,
        string? excludeMachineValue = null)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();
        await JournalDayBoundary.EnsureAsync(auth.Id, user.TimeZoneId, journals, ct);

        var filters = new CptFilterSet
        {
            IncludeCustomer = ReportEndpoints.ParseField(includeCustomer, includeCustomerValue),
            IncludeProject = ReportEndpoints.ParseField(includeProject, includeProjectValue),
            IncludeTask = ReportEndpoints.ParseField(includeTask, includeTaskValue),
            IncludeMachine = ReportEndpoints.ParseField(includeMachine, includeMachineValue),
            ExcludeCustomer = ReportEndpoints.ParseField(excludeCustomer, excludeCustomerValue),
            ExcludeProject = ReportEndpoints.ParseField(excludeProject, excludeProjectValue),
            ExcludeTask = ReportEndpoints.ParseField(excludeTask, excludeTaskValue),
            ExcludeMachine = ReportEndpoints.ParseField(excludeMachine, excludeMachineValue)
        };

        var hasDate = !string.IsNullOrWhiteSpace(fromDate) || !string.IsNullOrWhiteSpace(toDate);
        var hasCpt = filters.IncludeCustomer.IsActive || filters.IncludeProject.IsActive
            || filters.IncludeTask.IsActive || filters.IncludeMachine.IsActive
            || filters.ExcludeCustomer.IsActive || filters.ExcludeProject.IsActive
            || filters.ExcludeTask.IsActive || filters.ExcludeMachine.IsActive;

        if (!hasDate && !hasCpt)
        {
            var simple = await journals.ListForUserAsync(auth.Id, limit is > 0 and <= 5000 ? limit.Value : 200, ct);
            return Results.Ok(simple.Select(ToResponse));
        }

        var tzId = string.IsNullOrWhiteSpace(user.TimeZoneId) ? "UTC" : user.TimeZoneId.Trim();
        DateTimeOffset? rangeStart = null;
        DateTimeOffset? rangeEnd = null;
        if (hasDate)
        {
            var todayLocal = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SafeTz(tzId)));
            if (!TryParseLocalDate(fromDate, out var from)) from = todayLocal.AddDays(-6);
            if (!TryParseLocalDate(toDate, out var to)) to = todayLocal;
            if (from > to)
                return Results.BadRequest(new { error = "fromDate must be on or before toDate." });
            (rangeStart, _) = DayRange.ForLocalDate(from, tzId);
            (_, rangeEnd) = DayRange.ForLocalDate(to, tzId);
        }

        var take = limit is > 0 and <= 5000 ? limit.Value : 1000;
        var list = await journals.ListForUserFilteredAsync(
            auth.Id, rangeStart, rangeEnd, WellKnownIds.None, filters, take, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    private static bool TryParseLocalDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value?.Trim() ?? "",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static TimeZoneInfo SafeTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Returns one journal entry owned by the caller.
    /// </summary>
    /// <param name="id">Journal entry id.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="journals">Journal repository.</param>
    /// <param name="users">Profile timezone for day-boundary rollover.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with the entry, or 404 if missing/not owned.</returns>
    private static async Task<IResult> GetAsync(
        Uuid7Param id,
        ClaimsPrincipal principal,
        JournalRepository journals,
        UserRepository users,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();
        await JournalDayBoundary.EnsureAsync(auth.Id, user.TimeZoneId, journals, ct);
        var entry = await journals.GetForUserAsync(id, auth.Id, ct);
        return entry is null ? Results.NotFound() : Results.Ok(ToResponse(entry));
    }

    /// <summary>
    /// Starts a new open journal segment on a machine, auto-closing any previous open entry.
    /// </summary>
    /// <param name="body">Machine, optional target/notes, optional start time.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="journals">Journal repository.</param>
    /// <param name="machines">Validates machine and updates last seen.</param>
    /// <param name="lists">Resolves or creates a JournalTarget when not supplied.</param>
    /// <param name="settings">Reads <see cref="SettingKeys.JournalRecentLastSeenMinutes"/> for auto-close.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 with the new open entry; 400/409 on validation or unique-index conflict.</returns>
    /// <remarks>
    /// Web Journals page and future tray clients call this. Target defaults to NONE triple when omitted.
    /// </remarks>
    private static async Task<IResult> StartAsync(
        StartJournalRequest body,
        ClaimsPrincipal principal,
        JournalRepository journals,
        MachineRepository machines,
        ListRepository lists,
        SettingRepository settings,
        UserRepository users,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        if (body.MachineId == Uuid7.Empty)
            return Results.BadRequest(new { error = "machineId is required." });

        var machine = await machines.GetByIdAsync(body.MachineId, ct);
        if (machine is null)
            return Results.BadRequest(new { error = "Unknown machineId." });

        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();
        // Split any open entries that crossed local midnight before applying target change.
        await JournalDayBoundary.EnsureAsync(auth.Id, user.TimeZoneId, journals, ct);

        JournalTarget? resolvedTarget = null;
        var targetId = body.JournalTargetId;
        if (targetId is null || targetId == Uuid7.Empty)
        {
            resolvedTarget = await lists.GetOrCreateTargetAsync(
                body.CustomerId ?? WellKnownIds.None,
                body.ProjectId ?? WellKnownIds.None,
                body.TaskId ?? WellKnownIds.None,
                null, null, null, ct);
            targetId = resolvedTarget.Id;
        }
        else
        {
            resolvedTarget = await lists.GetTargetAsync(targetId.Value, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var start = body.StartUtc?.ToUniversalTime() ?? now;

        var open = await journals.GetOpenAsync(auth.Id, body.MachineId, ct);
        if (open is not null)
        {
            var closeAt = await ComputeCloseTimeAsync(open, machine, now, settings, ct);
            await journals.CloseAsync(open.Id, auth.Id, closeAt, ct);
        }

        var entryId = body.Id is { } cid && cid != Uuid7.Empty && cid != WellKnownIds.None
            ? cid
            : IdFactory.NewId();

        var tagCustomerId = resolvedTarget?.CustomerId
            ?? body.CustomerId
            ?? WellKnownIds.None;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, tagCustomerId, ct);

        var entry = new JournalEntry
        {
            Id = entryId,
            UserId = auth.Id,
            MachineId = body.MachineId,
            StartUtc = start,
            StopUtc = null,
            JournalTargetId = targetId.Value,
            Notes = body.Notes,
            Tags = string.IsNullOrEmpty(tags) ? null : tags,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await journals.InsertAsync(entry, ct);
        }
        catch
        {
            return Results.Conflict(new { error = "Could not start journal (open entry conflict?)." });
        }

        await machines.UpdateIdentityAsync(body.MachineId, null, null, null, now, ct);
        return Results.Created($"/api/v1/journals/{Uuid7Text.Format(entry.Id)}", ToResponse(entry));
    }

    /// <summary>
    /// Stops an open journal entry (sets <c>stop_utc</c>).
    /// </summary>
    /// <param name="id">Journal entry id.</param>
    /// <param name="body">Optional stop time; defaults to now.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="journals">Journal repository.</param>
    /// <param name="machines">Touches machine last seen on stop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with closed entry; 400 if already stopped or stop before start; 404 if missing.</returns>
    private static async Task<IResult> StopAsync(
        Uuid7Param id,
        StopJournalRequest? body,
        ClaimsPrincipal principal,
        JournalRepository journals,
        MachineRepository machines,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var entry = await journals.GetForUserAsync(id, auth.Id, ct);
        if (entry is null) return Results.NotFound();
        if (entry.StopUtc is not null)
            return Results.BadRequest(new { error = "Journal already stopped." });

        var stop = body?.StopUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        if (stop < entry.StartUtc)
            return Results.BadRequest(new { error = "Stop must be after start." });

        await journals.CloseAsync(id, auth.Id, stop, ct);
        await machines.UpdateIdentityAsync(entry.MachineId, null, null, null, stop, ct);
        entry = (await journals.GetForUserAsync(id, auth.Id, ct))!;
        return Results.Ok(ToResponse(entry));
    }

    /// <summary>
    /// Partially updates notes, tags, target, or times on a journal entry owned by the caller.
    /// </summary>
    /// <param name="id">Journal entry id.</param>
    /// <param name="body">Fields to change; null properties leave existing values.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="journals">Journal repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with updated entry, or 404.</returns>
    private static async Task<IResult> UpdateAsync(
        Uuid7Param id,
        UpdateJournalRequest body,
        ClaimsPrincipal principal,
        JournalRepository journals,
        ListRepository lists,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var entry = await journals.GetForUserAsync(id, auth.Id, ct);
        if (entry is null) return Results.NotFound();

        if (body.StartUtc is not null) entry.StartUtc = body.StartUtc.Value.ToUniversalTime();
        if (body.StopUtc is not null) entry.StopUtc = body.StopUtc.Value.ToUniversalTime();
        if (body.JournalTargetId is not null) entry.JournalTargetId = body.JournalTargetId.Value;
        if (body.Notes is not null) entry.Notes = body.Notes;
        if (body.Tags is not null)
        {
            var target = await lists.GetTargetAsync(entry.JournalTargetId, ct);
            var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(
                lists, body.Tags, target?.CustomerId, ct);
            entry.Tags = string.IsNullOrEmpty(tags) ? null : tags;
        }
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        await journals.UpdateAsync(entry, ct);
        return Results.Ok(ToResponse(entry));
    }

    /// <summary>
    /// Deletes a journal entry owned by the caller (clears screenshot links first).
    /// </summary>
    private static async Task<IResult> DeleteAsync(
        Uuid7Param id,
        ClaimsPrincipal principal,
        JournalRepository journals,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var entry = await journals.GetForUserAsync(id, auth.Id, ct);
        if (entry is null) return Results.NotFound();
        var deleted = await journals.DeleteAsync(id, auth.Id, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Chooses the stop timestamp when auto-closing a previous open journal on start.
    /// </summary>
    /// <param name="open">The open entry being closed.</param>
    /// <param name="machine">Machine row (provides <see cref="Machine.LastSeen"/>).</param>
    /// <param name="now">Current UTC time.</param>
    /// <param name="settings">Org settings for the recent-window minutes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>UTC close time to write to <c>stop_utc</c>.</returns>
    /// <remarks>
    /// Reads <see cref="SettingKeys.JournalRecentLastSeenMinutes"/> (default 30).
    /// Recent activity → close at <paramref name="now"/>; aged → close at LastSeen (not before start).
    /// </remarks>
    private static async Task<DateTimeOffset> ComputeCloseTimeAsync(
        JournalEntry open,
        Machine machine,
        DateTimeOffset now,
        SettingRepository settings,
        CancellationToken ct)
    {
        var minutes = await settings.GetIntAsync(
            SettingKeys.JournalRecentLastSeenMinutes,
            SettingKeys.Defaults.JournalRecentLastSeenMinutes,
            ct);

        var lastSeen = machine.LastSeen;
        if (lastSeen is null)
            return now;

        var age = now - lastSeen.Value;
        if (age.TotalMinutes <= minutes)
            return now;

        return lastSeen.Value < open.StartUtc ? open.StartUtc : lastSeen.Value;
    }

    /// <summary>
    /// Maps a domain journal entry to the public API DTO.
    /// </summary>
    /// <param name="e">Persisted entry.</param>
    /// <returns>Response without internal-only fields.</returns>
    private static JournalEntryResponse ToResponse(JournalEntry e) => new()
    {
        Id = e.Id,
        UserId = e.UserId,
        MachineId = e.MachineId,
        StartUtc = e.StartUtc,
        StopUtc = e.StopUtc,
        JournalTargetId = e.JournalTargetId,
        Notes = e.Notes,
        Tags = e.Tags,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
