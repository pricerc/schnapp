namespace Schnapp.Client.Linux.Services;

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
public sealed class JournalSessionService
{
    private readonly CaptureService _capture;
    private readonly ClientSettingsStore _store = new();

    public JournalSessionService(CaptureService capture)
    {
        _capture = capture;
        LoadFromCache();
    }

    public List<ListItemResponse> Customers { get; private set; } = [];
    public List<ListItemResponse> Projects { get; private set; } = [];
    public List<ListItemResponse> Tasks { get; private set; } = [];
    public List<JournalTargetResponse> Targets { get; private set; } = [];
    public JournalEntryResponse? OpenJournal { get; private set; }

    /// <summary>Local tag dictionary cache (for multi-tag autocomplete).</summary>
    public ReferenceDataCache ReferenceData => _capture.Uploads.ReferenceData;

    public event EventHandler? Changed;

    /// <summary>Projects belonging to <paramref name="customerName"/> (all projects if no customer chosen yet).</summary>
    public IReadOnlyList<ListItemResponse> ProjectsFor(string? customerName)
    {
        var customerId = ResolveId(Customers, customerName);
        if (customerId is null)
        {
            // No customer yet — show unscoped list so the user can still browse; Apply will pair later.
            return Projects;
        }

        // Strict filter: do not fall back to all projects (that reintroduces invalid C/P pairs).
        return [.. Projects.Where(p => p.CustomerId == customerId)];
    }

    /// <summary>Tasks belonging to <paramref name="customerName"/>/<paramref name="projectName"/> (unfiltered where a parent isn't chosen yet).</summary>
    public IReadOnlyList<ListItemResponse> TasksFor(string? customerName, string? projectName)
    {
        var customerId = ResolveId(Customers, customerName);
        var projectId = ResolveId(Projects, projectName);
        if (customerId is null && projectId is null)
        {
            return Tasks;
        }

        // Strict filter: empty is OK — never return unrelated tasks from other parents.
        return [.. Tasks.Where(t =>
            (customerId is null || t.CustomerId == customerId) &&
            (projectId is null || t.ProjectId == projectId))];
    }

    private static Uuid7? ResolveId(List<ListItemResponse> items, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>Loads the last-cached reference data (customers/projects/tasks/journal-targets) so the
    /// tray menu can populate instantly on launch, even before the first API refresh completes.</summary>
    private void LoadFromCache()
    {
        var cache = _capture.Uploads.ReferenceData;
        Customers = cache.GetCustomers();
        Projects = cache.GetProjects();
        Tasks = cache.GetTasks();
        Targets = cache.GetJournalTargets();
    }

    public async Task RefreshAsync()
    {
        using var api = CreateApi();
        await _capture.Uploads.RefreshReferenceDataAsync(api).ConfigureAwait(false);
        LoadFromCache();
        await RefreshOpenJournalAsync(api);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task TryRefreshAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // keep cache
        }
    }

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
    /// Formatted description of the currently-open journal's target (Customer / Project / Task,
    /// or the target's display name), or <c>null</c> if no journal is open.
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

    public (string Customer, string Project, string Task) NamesFor(JournalTargetResponse t)
    {
        var c = Customers.FirstOrDefault(x => x.Id == t.CustomerId)?.Name ?? "";
        var p = Projects.FirstOrDefault(x => x.Id == t.ProjectId)?.Name ?? "";
        var task = Tasks.FirstOrDefault(x => x.Id == t.TaskId)?.Name ?? "";
        return (c, p, task);
    }

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

    public async Task<string> ApplyMruAsync(JournalTargetResponse target, string? notes, string? tags) =>
        await ApplyTargetAsync(target.CustomerId, target.ProjectId, target.TaskId, target.Id, notes, tags, forceOffline: false);

    /// <summary>
    /// Resolves (or creates) customer/project/task and ensures a journal target without starting
    /// a live journal entry. Used when assigning targets to existing screenshots.
    /// </summary>
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
            catch
            {
                forceOffline = true;
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
            _capture.Uploads.Enqueue(MutationKind.JournalUpdate, new JournalUpdatePayload
            {
                JournalEntryId = OpenJournal.Id,
                Request = req
            });
            return (saveTags ? "Tags" : "Notes") + " queued offline (" + ex.GetType().Name + ").";
        }
    }

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

    private async Task<(Uuid7 Id, bool Queued)> ResolveOrCreateAsync(
        SchnappApiClient? api,
        string kind,
        string? name,
        List<ListItemResponse> cache,
        Uuid7? customerId,
        Uuid7? projectId,
        bool forceQueue)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Trim(), "NONE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name.Trim(), "-", StringComparison.Ordinal))
        {

            return (Uuid7.Empty, false);
        }


        var trimmed = name.Trim();
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
                // queue offline
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

    public sealed class MruEntry
    {
        public MruEntry(JournalTargetResponse target, string display)
        {
            Target = target;
            Display = display;
        }

        public JournalTargetResponse Target { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }
}
