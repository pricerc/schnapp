namespace Schnapp.Client.Windows.Services;

using Medo;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Ids;
using Schnapp.Client.Core.Tags;
using Schnapp.Client.Core.Upload;
using Schnapp.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Shared journal target load/apply/clear for MainWindow and the tray quick picker.
/// </summary>
/// <remarks>
/// Owns the in-memory customers/projects/tasks/targets cache and the currently open
/// <see cref="JournalEntryResponse"/>. Mutations go online when possible and fall back
/// to the offline mutation queue via <see cref="CaptureService.Uploads"/>.
/// </remarks>
public sealed class JournalSessionService
{
    private readonly CaptureService _capture;
    private readonly ClientSettingsStore _store = new();

    /// <summary>
    /// Creates a session bound to <paramref name="capture"/> and seeds lists from the
    /// local reference-data cache (no network call).
    /// </summary>
    /// <param name="capture">Capture service used for uploads/spool and reference-data access.</param>
    public JournalSessionService(CaptureService capture)
    {
        _capture = capture;
        LoadFromCache();
    }

    /// <summary>Cached customer list items (names/codes/ids).</summary>
    public List<ListItemResponse> Customers { get; private set; } = [];

    /// <summary>Cached project list items (optionally scoped by customer id).</summary>
    public List<ListItemResponse> Projects { get; private set; } = [];

    /// <summary>Cached task list items (optionally scoped by customer/project id).</summary>
    public List<ListItemResponse> Tasks { get; private set; } = [];

    /// <summary>Cached journal targets (customer+project+task combinations).</summary>
    public List<JournalTargetResponse> Targets { get; private set; } = [];

    /// <summary>Local tag dictionary cache (for multi-tag autocomplete).</summary>
    public ReferenceDataCache ReferenceData => _capture.Uploads.ReferenceData;

    /// <summary>
    /// Currently open journal entry for this machine, or <c>null</c> when none is open
    /// (or after a local clear / offline stop).
    /// </summary>
    public JournalEntryResponse? OpenJournal { get; private set; }

    /// <summary>
    /// Raised after open journal, targets, or list caches change (UI should refresh).
    /// May fire from background threads after async apply/refresh.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Projects belonging to <paramref name="customerName"/>.
    /// When no customer is chosen, returns the full list (browse / free type).
    /// </summary>
    /// <param name="customerName">Customer display name, or null/blank/(none) for unscoped.</param>
    /// <returns>Projects for that customer, or the full project list when unscoped.</returns>
    public IReadOnlyList<ListItemResponse> ProjectsFor(string? customerName)
    {
        var customerId = ResolveId(Customers, customerName);
        if (customerId is null)
        {
            // Because no customer yet — show unscoped list so the user can still browse; Apply will pair later.
            return Projects;
        }

        // Because strict filter: do not fall back to all projects (that reintroduces invalid C/P pairs).
        return [.. Projects.Where(p => p.CustomerId == customerId)];
    }

    /// <summary>
    /// Tasks belonging to <paramref name="customerName"/> / <paramref name="projectName"/>.
    /// </summary>
    /// <remarks>
    /// A blank / <c>(none)</c> project is a real filter value: only tasks with no project
    /// (<see cref="Uuid7.Empty"/> / null) are returned. When both parents are unset, returns
    /// the full task list for browsing.
    /// </remarks>
    /// <param name="customerName">Customer display name, or null/blank for unscoped customer.</param>
    /// <param name="projectName">Project display name, or null/blank/(none) for no-project tasks.</param>
    /// <returns>Filtered task list for the given parent scope.</returns>
    public IReadOnlyList<ListItemResponse> TasksFor(string? customerName, string? projectName)
    {
        var customerId = ResolveId(Customers, customerName);
        var projectIsNone = IsNoneName(projectName);
        var projectId = projectIsNone ? null : ResolveId(Projects, projectName);

        // Because nothing scoped yet — full list for browse / free type.
        if (customerId is null && projectIsNone)
        {
            return Tasks;
        }

        return [.. Tasks.Where(t =>
        {
            if (customerId is not null && t.CustomerId != customerId)
            {
                return false;
            }

            if (projectIsNone)
            {
                // Because blank project is valid: only tasks not linked to a project.
                return !t.ProjectId.HasValue || t.ProjectId.Value == Uuid7.Empty;
            }

            // Because named project not in cache yet → no matches (strict).
            if (projectId is null)
            {
                return false;
            }

            return t.ProjectId == projectId;
        })];
    }

    /// <summary>True for null/blank/<c>(none)</c> list names.</summary>
    /// <param name="name">Candidate list item name from UI.</param>
    /// <returns><c>true</c> when the name means “no selection”.</returns>
#pragma warning disable CS8602 // Dereference of a possibly null reference.
    private static bool IsNoneName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return string.Equals(name.Trim(), "(none)", StringComparison.OrdinalIgnoreCase);
    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

    /// <summary>
    /// Resolves a list item id by case-insensitive name match, or <c>null</c> when
    /// <paramref name="name"/> is none/blank or not found.
    /// </summary>
    /// <param name="items">Cached list items to search.</param>
    /// <param name="name">Display name to match.</param>
    /// <returns>Matching item id, or <c>null</c> when none/unknown.</returns>
    private static Uuid7? ResolveId(List<ListItemResponse> items, string? name)
    {
        if (IsNoneName(name))
        {
            return null;
        }

        return items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Loads the last-cached reference data (customers/projects/tasks/journal-targets) so the
    /// tray menu can populate instantly on launch, even before the first API refresh completes.
    /// </summary>
    private void LoadFromCache()
    {
        var cache = _capture.Uploads.ReferenceData;
        Customers = cache.GetCustomers();
        Projects = cache.GetProjects();
        Tasks = cache.GetTasks();
        Targets = cache.GetJournalTargets();
    }

    /// <summary>
    /// Refreshes list caches and open-journal state from the API, then raises <see cref="Changed"/>.
    /// </summary>
    /// <returns>A task that completes when refresh finishes.</returns>
    /// <exception cref="InvalidOperationException">Not logged in (no token).</exception>
    public async Task RefreshAsync()
    {
        using var api = CreateApi();
        await _capture.Uploads.RefreshReferenceDataAsync(api).ConfigureAwait(false);
        LoadFromCache();
        await RefreshOpenJournalAsync(api);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Best-effort <see cref="RefreshAsync"/>; swallows errors so startup can continue offline.
    /// </summary>
    /// <returns>A task that completes when the attempt finishes (success or failure).</returns>
    public async Task TryRefreshAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // Because keep cache when offline / API unavailable at launch.
        }
    }

    /// <summary>
    /// Builds the MRU journal-target list from settings, skipping missing or empty targets.
    /// </summary>
    /// <returns>Most-recently-used targets with display labels (newest first).</returns>
    public IReadOnlyList<MruEntry> BuildMru()
    {
        var settings = _store.Load();
        var ids = settings.JournalTargetMru ?? [];
        var list = new List<MruEntry>();
        foreach (var id in ids)
        {
            var t = Targets.FirstOrDefault(x => x.Id == id);
            if (t == null)
            {
                continue;
            }


            if (t.CustomerId == Uuid7.Empty && t.ProjectId == Uuid7.Empty && t.TaskId == Uuid7.Empty)
            {
                continue;
            }


            list.Add(new MruEntry(t, FormatTarget(t)));
        }
        return list;
    }

    /// <summary>
    /// Human-readable target label using list <em>names</em>
    /// (or the target's display name when set).
    /// </summary>
    /// <param name="t">Journal target to format.</param>
    /// <returns>Display name or <c>Customer / Project / Task</c> path.</returns>
    public string FormatTarget(JournalTargetResponse t)
    {
        var c = Customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Name
                ?? (t.CustomerId == Uuid7.Empty ? "(none)" : "?");
        var p = Projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Name
                ?? (t.ProjectId == Uuid7.Empty ? "(none)" : "?");
        var task = Tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Name
                   ?? (t.TaskId == Uuid7.Empty ? "(none)" : "?");
        if (!string.IsNullOrWhiteSpace(t.Name) && t.Name != "(none)")
        {
            return t.Name;
        }

        return $"{c} / {p} / {task}";
    }

    /// <summary>
    /// Compact target label using list <em>codes</em> (e.g. <c>ACME001-PROJ002-TSK003</c>),
    /// suitable for space-limited UI such as the tray tooltip.
    /// </summary>
    /// <remarks>
    /// Prefers <see cref="JournalTargetResponse.Code"/> when present; otherwise joins
    /// customer/project/task codes with <c>-</c>. Empty slots are omitted.
    /// </remarks>
    /// <param name="t">Journal target to format.</param>
    /// <returns>Short code path, or <c>?</c> when nothing resolvable.</returns>
    public string FormatTargetCodes(JournalTargetResponse t)
    {
        if (!string.IsNullOrWhiteSpace(t.Code) && t.Code != "(none)")
        {
            return t.Code.Trim();
        }

        var c = Customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Code;
        var p = Projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Code;
        var task = Tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Code;
        var parts = new[] { c, p, task }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim());
        var joined = string.Join("-", parts);
        return string.IsNullOrEmpty(joined) ? "?" : joined;
    }

    /// <summary>
    /// Formatted description of the currently-open journal's target (Customer / Project / Task
    /// names, or the target's display name), or <c>null</c> if no journal is open.
    /// </summary>
    public string? CurrentTargetDisplay
    {
        get
        {
            var open = OpenJournal;
            if (open == null)
            {
                return null;
            }

            var target = Targets.FirstOrDefault(t => t.Id == open.JournalTargetId);
            return target != null ? FormatTarget(target) : null;
        }
    }

    /// <summary>
    /// Short-code form of the open journal target for tray tooltips, or <c>null</c> if none open.
    /// </summary>
    public string? CurrentTargetCodes
    {
        get
        {
            var open = OpenJournal;
            if (open == null)
            {
                return null;
            }

            var target = Targets.FirstOrDefault(t => t.Id == open.JournalTargetId);
            return target != null ? FormatTargetCodes(target) : null;
        }
    }

    /// <summary>Display names for the customer / project / task of a journal target.</summary>
    /// <param name="t">Journal target whose parent list items to resolve.</param>
    /// <returns>Tuple of customer, project, and task names (empty string when unknown).</returns>
    public (string Customer, string Project, string Task) NamesFor(JournalTargetResponse t)
    {
        var c = Customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Name ?? "";
        var p = Projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Name ?? "";
        var task = Tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Name ?? "";
        return (c, p, task);
    }

    /// <summary>Short codes for the customer / project / task of a journal target.</summary>
    /// <param name="t">Journal target whose parent list items to resolve.</param>
    /// <returns>Tuple of customer, project, and task codes (empty string when unknown).</returns>
    public (string Customer, string Project, string Task) CodesFor(JournalTargetResponse t)
    {
        var c = Customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Code ?? "";
        var p = Projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Code ?? "";
        var task = Tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Code ?? "";
        return (c, p, task);
    }

    /// <summary>
    /// Resolves (or creates) customer/project/task by name, then starts a journal on that target.
    /// </summary>
    /// <param name="customer">Customer name, or null/(none) for empty.</param>
    /// <param name="project">Project name, or null/(none) for empty.</param>
    /// <param name="task">Task name, or null/(none) for empty.</param>
    /// <param name="notes">Optional notes applied on journal start.</param>
    /// <param name="tags">Optional tags (normalized) applied on journal start.</param>
    /// <returns>Human status message (online start or offline queue).</returns>
    public async Task<string> ApplyFromNamesAsync(string? customer, string? project, string? task, string? notes, string? tags)
    {
        var resolved = await ResolveOrCreateSelectionsAsync(customer, project, task);
        return await ApplyTargetAsync(
            resolved.CustomerId,
            resolved.ProjectId,
            resolved.TaskId,
            preferredTargetId: null,
            notes,
            tags,
            forceOffline: resolved.AnyQueued);
    }

    /// <summary>
    /// Starts a journal on an existing MRU target (reuses <paramref name="target"/> id when known).
    /// </summary>
    /// <param name="target">Cached journal target from MRU.</param>
    /// <param name="notes">Optional notes for the new entry.</param>
    /// <param name="tags">Optional tags for the new entry.</param>
    /// <returns>Human status message (online start or offline queue).</returns>
    public async Task<string> ApplyMruAsync(JournalTargetResponse target, string? notes, string? tags) =>
        await ApplyTargetAsync(target.CustomerId, target.ProjectId, target.TaskId, target.Id, notes, tags, forceOffline: false);

    /// <summary>
    /// Resolves (or creates) customer/project/task and ensures a journal target without starting
    /// a live journal entry. Used when assigning targets to existing screenshots.
    /// </summary>
    /// <param name="customer">Customer name, or null/(none).</param>
    /// <param name="project">Project name, or null/(none).</param>
    /// <param name="task">Task name, or null/(none).</param>
    /// <param name="preferredTargetId">Existing target id when known (MRU).</param>
    /// <returns>Target (always non-null for the none triple), whether creates were queued offline, and status text.</returns>
    public async Task<(JournalTargetResponse Target, bool AnyQueued, string Status)> EnsureTargetFromNamesAsync(
        string? customer, string? project, string? task, Uuid7? preferredTargetId = null)
    {
        var resolved = await ResolveOrCreateSelectionsAsync(customer, project, task);
        return await EnsureTargetAsync(
            resolved.CustomerId, resolved.ProjectId, resolved.TaskId,
            preferredTargetId, resolved.AnyQueued);
    }

    /// <summary>
    /// Ensures a journal target for known C/P/T ids without starting a journal entry.
    /// </summary>
    public async Task<(JournalTargetResponse Target, bool AnyQueued, string Status)> EnsureTargetAsync(
        Uuid7 customerId, Uuid7 projectId, Uuid7 taskId, Uuid7? preferredTargetId, bool forceOffline = false)
    {
        var targetId = preferredTargetId is { } tid && tid != Uuid7.Empty ? tid : IdFactory.NewId();

        if (!forceOffline)
        {
            try
            {
                using var api = CreateApi();
                JournalTargetResponse target;
                if (preferredTargetId is { } existingId && existingId != Uuid7.Empty
                    && Targets.FirstOrDefault(t => t.Id == existingId) is { } known)
                {
                    target = known;
                    targetId = known.Id;
                }
                else
                {
                    target = await api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
                    {
                        Id = preferredTargetId is { } p && p != Uuid7.Empty ? p : null,
                        CustomerId = customerId,
                        ProjectId = projectId,
                        TaskId = taskId
                    });
                    targetId = target.Id;
                }

                if (Targets.All(t => t.Id != target.Id))
                {
                    Targets = [.. (await api.ListJournalTargetsAsync())];
                    _capture.Uploads.ReferenceData.ReplaceJournalTargets(Targets);
                }

                PushMru(target.Id);
                Changed?.Invoke(this, EventArgs.Empty);
                return (target, false, "Target: " + FormatTarget(target));
            }
            catch (Exception ex)
            {
                forceOffline = true;
                // fall through
                _ = ex;
            }
        }

        var offline = new JournalTargetResponse
        {
            Id = targetId,
            CustomerId = customerId,
            ProjectId = projectId,
            TaskId = taskId,
            Name = ""
        };
        if (Targets.All(t => t.Id != targetId))
        {
            Targets.Add(offline);
        }

        _capture.Uploads.Enqueue(MutationKind.JournalTargetEnsure, new JournalTargetEnsurePayload
        {
            Request = new JournalTargetRequest
            {
                Id = targetId,
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId
            }
        });
        PushMru(targetId);
        Changed?.Invoke(this, EventArgs.Empty);
        return (offline, true, "Target queued offline.");
    }

    /// <summary>
    /// Finds a journal entry for a target: open entry preferred, else most recent by start time.
    /// Offline: falls back to the local open journal when it matches the target.
    /// </summary>
    public async Task<Uuid7?> FindJournalEntryIdForTargetAsync(Uuid7 targetId)
    {
        if (targetId == Uuid7.Empty)
        {
            return null;
        }

        try
        {
            using var api = CreateApi();
            var list = await api.ListJournalsAsync();
            var open = list.FirstOrDefault(j => j.IsOpen && j.JournalTargetId == targetId);
            if (open != null)
            {
                return open.Id;
            }

            return list
                .Where(j => j.JournalTargetId == targetId)
                .OrderByDescending(j => j.StartUtc)
                .Select(j => (Uuid7?)j.Id)
                .FirstOrDefault();
        }
        catch
        {
            if (OpenJournal != null && OpenJournal.JournalTargetId == targetId)
            {
                return OpenJournal.Id;
            }

            return null;
        }
    }

    /// <summary>
    /// Stops the open journal (online or offline queue) and clears local open state.
    /// </summary>
    /// <returns>Status message describing clear success or offline queue.</returns>
    public async Task<string> ClearAsync()
    {
        var settings = _store.Load();
        if (!settings.MachineId.HasValue)
        {

            return "No machine selected.";
        }


        var queued = false;
        if (OpenJournal != null)
        {
            try
            {
                using var api = CreateApi();
                await api.StopJournalAsync(OpenJournal.Id);
            }
            catch (Exception ex)
            {
                // Because stop failed online — enqueue offline stop so the server still closes the entry later.
                _capture.Uploads.Enqueue(MutationKind.JournalStop, new JournalStopPayload
                {
                    JournalEntryId = OpenJournal.Id,
                    StopUtc = DateTimeOffset.UtcNow
                });
                queued = true;
                OpenJournal = null;
                Changed?.Invoke(this, EventArgs.Empty);
                return "Journal clear queued offline (" + ex.GetType().Name + ").";
            }
        }

        OpenJournal = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return queued ? "Journal clear queued offline." : "Journal cleared.";
    }

    /// <summary>
    /// Updates notes and/or tags on the open journal (online, or queued offline).
    /// </summary>
    /// <param name="notes">New notes when <paramref name="saveNotes"/> is true.</param>
    /// <param name="tags">New tags when <paramref name="saveTags"/> is true (normalized).</param>
    /// <param name="saveNotes">When true, include notes in the update payload.</param>
    /// <param name="saveTags">When true, include tags in the update payload.</param>
    /// <returns>Status message for the UI.</returns>
    public async Task<string> SaveFieldsAsync(string? notes, string? tags, bool saveNotes, bool saveTags)
    {
        if (OpenJournal == null)
        {

            return "No open journal.";
        }


        var req = new UpdateJournalRequest();
        if (saveNotes)
        {
            req.Notes = notes;
        }

        if (saveTags)
        {
            req.Tags = TagNormalizer.Normalize(tags ?? "");
        }


        try
        {
            using var api = CreateApi();
            OpenJournal = await api.UpdateJournalAsync(OpenJournal.Id, req);
            Changed?.Invoke(this, EventArgs.Empty);
            return saveTags ? "Tags saved." : "Notes saved.";
        }
        catch (Exception ex)
        {
            // Because online update failed — queue mutation so notes/tags are not lost.
            _capture.Uploads.Enqueue(MutationKind.JournalUpdate, new JournalUpdatePayload
            {
                JournalEntryId = OpenJournal.Id,
                Request = req
            });
            return (saveTags ? "Tags" : "Notes") + " queued offline (" + ex.GetType().Name + ").";
        }
    }

    /// <summary>
    /// Loads the open journal for the configured machine (or any open entry if no machine).
    /// </summary>
    /// <param name="api">Authenticated API client.</param>
    private async Task RefreshOpenJournalAsync(SchnappApiClient api)
    {
        var settings = _store.Load();
        // Server List closes open journals that started on a previous local day and clones for today.
        var list = await api.ListJournalsAsync();
        OpenJournal = settings.MachineId.HasValue
            ? list.FirstOrDefault(j => j.IsOpen && j.MachineId == settings.MachineId.Value)
            : list.FirstOrDefault(j => j.IsOpen);
    }

    /// <summary>
    /// If the cached open journal started on a previous local calendar day, refresh from the API
    /// (server day-boundary) so captures link to today's clone. No-op when already current or offline
    /// refresh fails (upload remap still fixes stale ids).
    /// </summary>
    public async Task EnsureCurrentDayAsync()
    {
        if (OpenJournal is null)
        {
            return;
        }

        var settings = _store.Load();
        var tzId = string.IsNullOrWhiteSpace(settings.TimeZoneId)
            ? TimeZoneInfo.Local.Id
            : settings.TimeZoneId!;
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { tz = TimeZoneInfo.Local; }

        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(OpenJournal.StartUtc.UtcDateTime, tz).Date;
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        if (startLocal >= todayLocal)
        {
            return;
        }

        try
        {
            using var api = CreateApi();
            await RefreshOpenJournalAsync(api).ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Keep stale OpenJournal; server upload remaps when online.
        }
    }

    /// <summary>
    /// Ensures a journal target and starts an entry online, or queues both offline.
    /// </summary>
    /// <param name="customerId">Customer id (may be empty).</param>
    /// <param name="projectId">Project id (may be empty).</param>
    /// <param name="taskId">Task id (may be empty).</param>
    /// <param name="preferredTargetId">Existing target id when known (MRU); otherwise new id.</param>
    /// <param name="notes">Optional notes for the start request.</param>
    /// <param name="tagsText">Raw tags text (normalized before send).</param>
    /// <param name="forceOffline">When true, skip the online path (e.g. list create was queued).</param>
    /// <returns>Human status message for tray/main UI.</returns>
    private async Task<string> ApplyTargetAsync(
        Uuid7 customerId,
        Uuid7 projectId,
        Uuid7 taskId,
        Uuid7? preferredTargetId,
        string? notes,
        string? tagsText,
        bool forceOffline = false)
    {
        var settings = _store.Load();
        if (!settings.MachineId.HasValue)
        {

            throw new InvalidOperationException("No machine selected. Open Machines… first.");
        }


        var tags = TagNormalizer.Normalize(tagsText ?? "");
        var entryId = IdFactory.NewId();
        var targetId = preferredTargetId is { } tid && tid != Uuid7.Empty ? tid : IdFactory.NewId();

        if (!forceOffline)
        {
            try
            {
                using var api = CreateApi();
                JournalTargetResponse target;
                if (preferredTargetId is { } existingId && existingId != Uuid7.Empty
                    && Targets.FirstOrDefault(t => t.Id == existingId) is { } known)
                {
                    target = known;
                    targetId = known.Id;
                }
                else
                {
                    target = await api.GetOrCreateJournalTargetAsync(new JournalTargetRequest
                    {
                        Id = preferredTargetId is { } p && p != Uuid7.Empty ? p : null,
                        CustomerId = customerId,
                        ProjectId = projectId,
                        TaskId = taskId
                    });
                    targetId = target.Id;
                }

                if (Targets.All(t => t.Id != target.Id))
                {
                    // Because target list is stale — refresh so FormatTarget/MRU resolve correctly.
                    Targets = [.. (await api.ListJournalTargetsAsync())];
                    _capture.Uploads.ReferenceData.ReplaceJournalTargets(Targets);
                }


                PushMru(target.Id);

                var entry = await api.StartJournalAsync(new StartJournalRequest
                {
                    Id = entryId,
                    MachineId = settings.MachineId.Value,
                    JournalTargetId = target.Id,
                    Notes = notes,
                    Tags = tags
                });
                OpenJournal = entry;
                Changed?.Invoke(this, EventArgs.Empty);
                return "Journal started: " + FormatTarget(target);
            }
            catch (Exception ex)
            {
                QueueOfflineJournalStart(settings.MachineId.Value, customerId, projectId, taskId, targetId, entryId, notes, tags);
                return "Online apply failed (" + ex.GetType().Name + "); queued offline.";
            }
        }

        QueueOfflineJournalStart(settings.MachineId.Value, customerId, projectId, taskId, targetId, entryId, notes, tags);
        return "Journal queued offline. Push offline spool when online.";
    }

    /// <summary>
    /// Enqueues journal-target ensure + journal start mutations and optimistically sets
    /// <see cref="OpenJournal"/> so captures attach the local entry id immediately.
    /// </summary>
    /// <param name="machineId">Machine that owns the journal entry.</param>
    /// <param name="customerId">Customer id for the target.</param>
    /// <param name="projectId">Project id for the target.</param>
    /// <param name="taskId">Task id for the target.</param>
    /// <param name="targetId">Client-generated target id shared by both mutations.</param>
    /// <param name="entryId">Client-generated journal entry id.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="tags">Already-normalized tags string.</param>
    private void QueueOfflineJournalStart(
        Uuid7 machineId,
        Uuid7 customerId,
        Uuid7 projectId,
        Uuid7 taskId,
        Uuid7 targetId,
        Uuid7 entryId,
        string? notes,
        string tags)
    {
        _capture.Uploads.Enqueue(MutationKind.JournalTargetEnsure, new JournalTargetEnsurePayload
        {
            Request = new JournalTargetRequest
            {
                Id = targetId,
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId
            }
        });
        _capture.Uploads.Enqueue(MutationKind.JournalStart, new JournalStartPayload
        {
            Request = new StartJournalRequest
            {
                Id = entryId,
                MachineId = machineId,
                JournalTargetId = targetId,
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId,
                Notes = notes,
                Tags = tags,
                StartUtc = DateTimeOffset.UtcNow
            }
        });

        // Because UI and capture need an open entry immediately even before the server ack.
        OpenJournal = new JournalEntryResponse
        {
            Id = entryId,
            UserId = Uuid7.Empty,
            MachineId = machineId,
            StartUtc = DateTimeOffset.UtcNow,
            JournalTargetId = targetId,
            Notes = notes,
            Tags = tags,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (Targets.All(t => t.Id != targetId))
        {
            Targets.Add(new JournalTargetResponse
            {
                Id = targetId,
                CustomerId = customerId,
                ProjectId = projectId,
                TaskId = taskId,
                Name = ""
            });
        }
        PushMru(targetId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves customer/project/task names to ids, creating list items online or offline as needed.
    /// </summary>
    /// <param name="customerName">Customer name (or none).</param>
    /// <param name="projectName">Project name (or none).</param>
    /// <param name="taskName">Task name (or none).</param>
    /// <returns>Resolved ids plus whether any create was queued offline.</returns>
    private async Task<(Uuid7 CustomerId, Uuid7 ProjectId, Uuid7 TaskId, bool AnyQueued)> ResolveOrCreateSelectionsAsync(
        string? customerName, string? projectName, string? taskName)
    {
        SchnappApiClient? api = null;
        try { api = CreateApi(); }
        catch { /* offline */ }

        var anyQueued = false;
        try
        {
            var c = await ResolveOrCreateAsync(api, "customers", customerName, Customers, null, null, forceQueue: api == null);
            anyQueued |= c.Queued;
            // Because once any parent was queued offline, children must also queue to keep id graphs consistent.
            var p = await ResolveOrCreateAsync(
                api, "projects", projectName, Projects, c.Id, null, forceQueue: anyQueued || api == null);
            anyQueued |= p.Queued;
            var t = await ResolveOrCreateAsync(
                api, "tasks", taskName, Tasks, c.Id, p.Id, forceQueue: anyQueued || api == null);
            anyQueued |= t.Queued;

            if (api != null && !anyQueued)
            {
                try
                {
                    // Because refresh after creates so codes/names match server-assigned values.
                    Customers = [.. (await api.ListItemsAsync("customers"))];
                    Projects = [.. (await api.ListItemsAsync("projects"))];
                    Tasks = [.. (await api.ListItemsAsync("tasks"))];
                    var cache = _capture.Uploads.ReferenceData;
                    cache.ReplaceCustomers(Customers);
                    cache.ReplaceProjects(Projects);
                    cache.ReplaceTasks(Tasks);
                }
                catch { /* keep local cache */ }
            }

            return (c.Id, p.Id, t.Id, anyQueued);
        }
        finally
        {
            api?.Dispose();
        }
    }

    /// <summary>
    /// Finds a list item by name/code in <paramref name="cache"/>, or creates it via API/queue.
    /// </summary>
    /// <param name="api">API client when online; null forces queue path.</param>
    /// <param name="kind">List kind: <c>customers</c>, <c>projects</c>, or <c>tasks</c>.</param>
    /// <param name="name">User-entered name (or none synonyms).</param>
    /// <param name="cache">In-memory list to search and update.</param>
    /// <param name="customerId">Parent customer for projects/tasks when creating.</param>
    /// <param name="projectId">Parent project for tasks when creating.</param>
    /// <param name="forceQueue">When true, skip the online create attempt.</param>
    /// <returns>Resolved id and whether the create was queued offline.</returns>
    private async Task<(Uuid7 Id, bool Queued)> ResolveOrCreateAsync(
        SchnappApiClient? api,
        string kind,
        string? name,
        List<ListItemResponse> cache,
        Uuid7? customerId,
        Uuid7? projectId,
        bool forceQueue)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (Uuid7.Empty, false);
        }

        var trimmed = name!.Trim();
        if (string.Equals(trimmed, "(none)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "NONE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "-", StringComparison.Ordinal))
        {
            return (Uuid7.Empty, false);
        }

        var existing = cache.FirstOrDefault(x =>
            string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Code, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {

            return (existing.Id, false);
        }


        var code = ListCodeGenerator.Allocate(trimmed, cache.Select(x => x.Code));
        var id = IdFactory.NewId();
        var item = new ListItemRequest
        {
            Id = id,
            Code = code,
            Name = trimmed,
            CustomerId = customerId,
            ProjectId = projectId
        };

        if (!forceQueue && api != null)
        {
            try
            {
                var created = await api.CreateListItemAsync(kind, item);
                cache.Add(created);
                return (created.Id, false);
            }
            catch
            {
                // Because create failed online — fall through to offline queue.
            }
        }

        _capture.Uploads.Enqueue(MutationKind.ListCreate, new ListCreatePayload
        {
            Kind = kind,
            Item = item
        });
        cache.Add(new ListItemResponse
        {
            Id = id,
            Code = code,
            Name = trimmed,
            CustomerId = customerId,
            ProjectId = projectId
        });
        return (id, true);
    }

    /// <summary>
    /// Inserts <paramref name="targetId"/> at the front of the journal-target MRU (max 20).
    /// </summary>
    /// <param name="targetId">Target to promote; empty ids are ignored.</param>
    private void PushMru(Uuid7 targetId)
    {
        if (targetId == Uuid7.Empty)
        {
            return;
        }


        var settings = _store.Load();
        var list = settings.JournalTargetMru ?? [];
        list.Remove(targetId);
        list.Insert(0, targetId);
        while (list.Count > 20)
        {
            list.RemoveAt(list.Count - 1);
        }


        settings.JournalTargetMru = list;
        _store.Save(settings);
    }

    /// <summary>
    /// Builds an authenticated API client from settings + token (env or store).
    /// </summary>
    /// <returns>Disposable API client.</returns>
    /// <exception cref="InvalidOperationException">When no login token is available.</exception>
    private SchnappApiClient CreateApi()
    {
        var s = _store.Load();
        var token = Environment.GetEnvironmentVariable("SCHNAPP_TOKEN") ?? _store.LoadToken();
        if (string.IsNullOrEmpty(token))
        {

            throw new InvalidOperationException("Not logged in.");
        }


        return new SchnappApiClient(s.ApiBaseUrl, token);
    }

    /// <summary>
    /// One MRU journal-target row for tray menus.
    /// </summary>
    public sealed class MruEntry
    {
        /// <summary>
        /// Creates an MRU entry with a preformatted display string.
        /// </summary>
        /// <param name="target">Underlying journal target.</param>
        /// <param name="display">Human label shown in the menu.</param>
        public MruEntry(JournalTargetResponse target, string display)
        {
            Target = target;
            Display = display;
        }

        /// <summary>Journal target to re-apply when the user picks this MRU item.</summary>
        public JournalTargetResponse Target { get; }

        /// <summary>Menu text (usually <see cref="FormatTarget"/>).</summary>
        public string Display { get; }

        /// <summary>Returns <see cref="Display"/> for WinForms default ToString binding.</summary>
        /// <returns>The display label.</returns>
        public override string ToString() => Display;
    }
}
