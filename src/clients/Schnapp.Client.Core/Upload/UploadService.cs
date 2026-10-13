namespace Schnapp.Client.Core.Upload;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Ids;
using Schnapp.Client.Core.Spool;
using Schnapp.Contracts;

using Medo;

/// <summary>
/// Durable offline queue for journal and list mutations, plus the local reference-data cache
/// (customers/projects/tasks/journal-targets). Mutations live in the unified client SQLite DB
/// (<c>mutation</c> table in <c>schnapp.db</c>); leftover filesystem queues under
/// <c>{data}/mutations/</c> are imported once on construction.
/// </summary>
/// <remarks>
/// Push order: list creates (customers → projects → tasks), journal-target ensure,
/// then journal start / update / stop, then screenshot metadata patches, by enqueue time
/// within each kind.
/// </remarks>
public sealed class UploadService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    }.AddUuid7Converters();

    private readonly MutationStore _store;
    private readonly ReferenceDataCache _referenceData;
    private readonly string _legacyRoot;
    private readonly string _databasePath;

    /// <summary>
    /// Creates an upload service using the unified client database for the mutation queue.
    /// </summary>
    /// <param name="rootDirectory">
    /// Optional legacy mutations directory (for one-time import and tests). Default
    /// <see cref="ClientPaths.GetMutationsDirectory"/>.
    /// </param>
    /// <param name="settingsMutationsDirectory">Optional path from <c>ClientSettings.MutationsDirectory</c>.</param>
    /// <param name="referenceDataDbPath">
    /// SQLite path for mutations + reference cache (default <see cref="ClientPaths.GetDatabasePath"/>).
    /// </param>
    public UploadService(string? rootDirectory = null, string? settingsMutationsDirectory = null, string? referenceDataDbPath = null)
    {
        _databasePath = referenceDataDbPath ?? ClientPaths.GetDatabasePath();
        _legacyRoot = rootDirectory ?? ClientPaths.GetMutationsDirectory(settingsMutationsDirectory);
        _store = new MutationStore(_databasePath);
        _referenceData = new ReferenceDataCache(_databasePath);
        ImportLegacyFilesystemQueue();
    }

    /// <summary>SQLite database path holding the mutation table.</summary>
    public string DatabasePath => _databasePath;

    /// <summary>Legacy filesystem queue root (only used for one-time migration).</summary>
    public string RootPath => _legacyRoot;

    /// <summary>Local cache of customers/projects/tasks/journal-targets pulled from the API.</summary>
    public ReferenceDataCache ReferenceData => _referenceData;

    /// <summary>
    /// Pulls customers/projects/tasks/journal-targets from the API and replaces the local cache.
    /// Throws on failure (caller decides whether to swallow and keep the last-cached copy).
    /// </summary>
    public async Task RefreshReferenceDataAsync(SchnappApiClient api, CancellationToken ct = default)
    {
        if (api == null)
        {
            throw new ArgumentNullException(nameof(api));
        }

        var customers = await api.ListItemsAsync("customers", ct).ConfigureAwait(false);
        var projects = await api.ListItemsAsync("projects", ct).ConfigureAwait(false);
        var tasks = await api.ListItemsAsync("tasks", ct).ConfigureAwait(false);
        var targets = await api.ListJournalTargetsAsync(ct).ConfigureAwait(false);
        var tags = await api.ListTagsAsync(customerId: null, ct).ConfigureAwait(false);

        _referenceData.ReplaceCustomers(customers);
        _referenceData.ReplaceProjects(projects);
        _referenceData.ReplaceTasks(tasks);
        _referenceData.ReplaceJournalTargets(targets);
        _referenceData.ReplaceTags(tags);
    }

    /// <summary>Number of pending (non-dead) mutations still awaiting push.</summary>
    public int PendingCount => _store.CountPending();

    /// <summary>
    /// Number of mutations that failed permanently (e.g. a 400/404/409/422 business-logic
    /// rejection) and are no longer retried automatically. See <see cref="MutationItem.Dead"/>.
    /// </summary>
    public int DeadCount => _store.CountDead();

    /// <summary>Dead (permanently failed) mutations, for inspection/manual cleanup.</summary>
    public IReadOnlyList<MutationItem> ListDeadItems() =>
        [.. ListItems().Where(i => i.Dead)];

    /// <summary>Deletes all dead (permanently failed) mutations. Returns the number removed.</summary>
    public int ClearDeadItems() =>
        DeleteItems([.. ListDeadItems().Select(i => i.Id)]);

    /// <summary>
    /// HTTP status codes that indicate a business-logic rejection that will never succeed by
    /// blindly retrying the same payload (as opposed to transient errors like connection
    /// refused, timeouts, or 5xx server errors, which stay in the normal backoff/retry path).
    /// </summary>
    private static readonly HashSet<int> PermanentStatusCodes = [400, 404, 409, 422];

    /// <summary>
    /// Enqueues a mutation with a JSON-serialized payload.
    /// </summary>
    /// <param name="kind">Mutation kind.</param>
    /// <param name="payload">Payload object matching the kind.</param>
    /// <returns>Mutation id.</returns>
    public Uuid7 Enqueue(MutationKind kind, object payload)
    {
        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var id = IdFactory.NewId();
        var item = new MutationItem
        {
            Id = id,
            Kind = kind,
            EnqueuedUtc = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions),
            Attempts = 0
        };
        _store.Upsert(item);
        return id;
    }

    /// <summary>Lists pending mutations in push order.</summary>
    public IReadOnlyList<MutationItem> ListItems() =>
        [.. _store.ListAll()
            .OrderBy(PushSortKey)
            .ThenBy(i => i.EnqueuedUtc)];

    /// <summary>Deletes mutations by id.</summary>
    public int DeleteItems(IEnumerable<Uuid7> ids) => _store.Delete(ids);

    /// <summary>
    /// Result of <see cref="ApplyScreenshotMetadataAsync"/>: local spool updates plus
    /// immediate server PATCHes and/or durable offline queues for already-uploaded items.
    /// </summary>
    public sealed class ScreenshotMetadataApplyResult
    {
        /// <summary>Spool items whose local upload.json was updated.</summary>
        public int LocalOk { get; set; }

        /// <summary>Already-uploaded items successfully PATCHed on the server.</summary>
        public int ServerOk { get; set; }

        /// <summary>
        /// Already-uploaded items whose server update was queued for later push
        /// (offline or transient failure).
        /// </summary>
        public int ServerQueued { get; set; }

        /// <summary>Last server/queue error message, if any.</summary>
        public string? LastError { get; set; }
    }

    /// <summary>
    /// Writes notes/tags/journal onto local spool items. Pending items pick this up on the
    /// next image upload. Already-uploaded items are PATCHed online immediately, or a
    /// <see cref="MutationKind.ScreenshotUpdate"/> is enqueued when offline/API fails.
    /// </summary>
    /// <param name="spool">Spool owning the item folders/index.</param>
    /// <param name="spoolItemIds">Local spool item ids to update.</param>
    /// <param name="notes">Replacement notes.</param>
    /// <param name="tags">Replacement tags (caller should normalize).</param>
    /// <param name="setJournal">When true, apply <paramref name="journalEntryId"/> (null clears).</param>
    /// <param name="journalEntryId">Journal entry link when <paramref name="setJournal"/> is true.</param>
    /// <param name="api">
    /// Optional authenticated client. When null, uploaded items are always queued offline.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ScreenshotMetadataApplyResult> ApplyScreenshotMetadataAsync(
        ScreenshotSpool spool,
        IReadOnlyList<Uuid7> spoolItemIds,
        string? notes,
        string? tags,
        bool setJournal,
        Uuid7? journalEntryId,
        SchnappApiClient? api = null,
        CancellationToken ct = default)
    {
        if (spool == null)
        {
            throw new ArgumentNullException(nameof(spool));
        }

        if (spoolItemIds == null)
        {
            throw new ArgumentNullException(nameof(spoolItemIds));
        }

        var result = new ScreenshotMetadataApplyResult();
        var request = new UpdateScreenshotRequest
        {
            Notes = notes,
            Tags = tags,
            SetJournalEntryId = setJournal,
            JournalEntryId = journalEntryId
        };

        foreach (var id in spoolItemIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!spool.UpdateUploadMetadata(id, notes, tags, setJournal, journalEntryId))
            {
                continue;
            }

            result.LocalOk++;

            var item = spool.GetItem(id);
            if (item == null || item.UploadStatus != SpoolUploadStatus.Uploaded)
            {
                // Pending: next spool PushAsync sends updated upload.json with the image.
                continue;
            }

            var serverId = item.ServerScreenshotId is { } sid && sid != Uuid7.Empty ? sid : id;

            if (api == null)
            {
                Enqueue(MutationKind.ScreenshotUpdate, new ScreenshotUpdatePayload
                {
                    ScreenshotId = serverId,
                    Request = request
                });
                result.ServerQueued++;
                continue;
            }

            try
            {
                await api.UpdateScreenshotAsync(serverId, request, ct).ConfigureAwait(false);
                result.ServerOk++;
            }
            catch (Exception ex)
            {
                Enqueue(MutationKind.ScreenshotUpdate, new ScreenshotUpdatePayload
                {
                    ScreenshotId = serverId,
                    Request = request
                });
                result.ServerQueued++;
                result.LastError = ex.Message;
            }
        }

        return result;
    }

    /// <summary>
    /// Pushes pending mutations to the API in dependency order.
    /// </summary>
    /// <param name="api">Authenticated API client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Succeeded and failed counts for this run.</returns>
    public async Task<(int Succeeded, int Failed)> PushAsync(SchnappApiClient api, CancellationToken ct = default)
    {
        if (api == null)
        {
            throw new ArgumentNullException(nameof(api));
        }

        var succeeded = 0;
        var failed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in ListItems())
        {
            ct.ThrowIfCancellationRequested();
            if (item.Dead)
            {
                continue;
            }

            if (item.NextAttemptUtc.HasValue && item.NextAttemptUtc.Value > now)
            {
                continue;
            }

            try
            {
                await ApplyAsync(api, item, ct).ConfigureAwait(false);
                DeleteItems(new[] { item.Id });
                succeeded++;
            }
            catch (ApiException apiEx) when (PermanentStatusCodes.Contains(apiEx.StatusCode))
            {
                // Business-logic rejection — mark dead instead of retrying forever.
                item.Attempts++;
                item.LastError = apiEx.Message;
                item.LastAttemptUtc = now;
                item.NextAttemptUtc = null;
                item.Dead = true;
                _store.Upsert(item);
            }
            catch (Exception ex)
            {
                failed++;
                item.Attempts++;
                item.LastError = ex.Message;
                item.LastAttemptUtc = now;
                var delaySec = Math.Min(3600, 5 * (1 << Math.Min(item.Attempts, 10)));
                item.NextAttemptUtc = now.AddSeconds(delaySec);
                _store.Upsert(item);
            }
        }

        return (succeeded, failed);
    }

    /// <summary>Closes the mutation store and reference-data cache connections.</summary>
    public void Dispose()
    {
        _store.Dispose();
        _referenceData.Dispose();
    }

    /// <summary>
    /// One-time import of legacy <c>{data}/mutations/{id}/meta.json</c> folders into SQLite,
    /// then removes successfully imported directories.
    /// </summary>
    private void ImportLegacyFilesystemQueue()
    {
        if (!Directory.Exists(_legacyRoot))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(_legacyRoot))
        {
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<MutationItem>(File.ReadAllText(metaPath), JsonOptions);
                if (item == null || item.Id == Uuid7.Empty)
                {
                    continue;
                }

                _store.Upsert(item);
                try { Directory.Delete(dir, true); } catch { /* leave for next run */ }
            }
            catch
            {
                // skip corrupt folder
            }
        }
    }

    private static int PushSortKey(MutationItem item)
    {
        if (item.Kind == MutationKind.ListCreate)
        {
            try
            {
                var p = JsonSerializer.Deserialize<ListCreatePayload>(item.PayloadJson, JsonOptions);
                if (p != null)
                {
                    if (string.Equals(p.Kind, "customers", StringComparison.OrdinalIgnoreCase))
                    {
                        return 10;
                    }

                    if (string.Equals(p.Kind, "projects", StringComparison.OrdinalIgnoreCase))
                    {
                        return 20;
                    }

                    if (string.Equals(p.Kind, "tasks", StringComparison.OrdinalIgnoreCase))
                    {
                        return 30;
                    }
                }
            }
            catch
            {
                // fall through
            }
            return 35;
        }

        return item.Kind switch
        {
            MutationKind.JournalTargetEnsure => 40,
            MutationKind.JournalStart => 50,
            MutationKind.JournalUpdate => 60,
            MutationKind.JournalStop => 70,
            // After journal mutations so journal links on screenshots can resolve when possible.
            MutationKind.ScreenshotUpdate => 80,
            _ => 90
        };
    }

    private static async Task ApplyAsync(SchnappApiClient api, MutationItem item, CancellationToken ct)
    {
        switch (item.Kind)
        {
            case MutationKind.ListCreate:
            {
                var p = JsonSerializer.Deserialize<ListCreatePayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad ListCreate payload");
                try
                {
                    await api.CreateListItemAsync(p.Kind, p.Item, ct).ConfigureAwait(false);
                }
                catch (ApiException ex) when (ex.StatusCode == 409)
                {
                    // Already created (retry / partial flush) — treat as success so dependents can proceed
                }
                break;
            }
            case MutationKind.JournalTargetEnsure:
            {
                var p = JsonSerializer.Deserialize<JournalTargetEnsurePayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad JournalTargetEnsure payload");
                await api.GetOrCreateJournalTargetAsync(p.Request, ct).ConfigureAwait(false);
                break;
            }
            case MutationKind.JournalStart:
            {
                var p = JsonSerializer.Deserialize<JournalStartPayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad JournalStart payload");
                await api.StartJournalAsync(p.Request, ct).ConfigureAwait(false);
                break;
            }
            case MutationKind.JournalStop:
            {
                var p = JsonSerializer.Deserialize<JournalStopPayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad JournalStop payload");
                await api.StopJournalAsync(p.JournalEntryId, ct).ConfigureAwait(false);
                break;
            }
            case MutationKind.JournalUpdate:
            {
                var p = JsonSerializer.Deserialize<JournalUpdatePayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad JournalUpdate payload");
                await api.UpdateJournalAsync(p.JournalEntryId, p.Request, ct).ConfigureAwait(false);
                break;
            }
            case MutationKind.ScreenshotUpdate:
            {
                var p = JsonSerializer.Deserialize<ScreenshotUpdatePayload>(item.PayloadJson, JsonOptions)
                        ?? throw new InvalidOperationException("Bad ScreenshotUpdate payload");
                await api.UpdateScreenshotAsync(p.ScreenshotId, p.Request, ct).ConfigureAwait(false);
                break;
            }
            default:
                throw new InvalidOperationException("Unknown mutation kind " + item.Kind);
        }
    }
}
