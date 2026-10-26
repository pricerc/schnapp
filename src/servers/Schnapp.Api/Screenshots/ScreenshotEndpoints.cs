using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

using Schnapp.Api.Auth;
using Schnapp.Api.Journals;
using Schnapp.Api.Tags;
using Schnapp.Contracts;
using Schnapp.Imaging;
using Schnapp.Data.Blobs;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;
using Schnapp.Screenshots;

using Medo;
namespace Schnapp.Api.Screenshots;

/// <summary>
/// Screenshot upload, list, get, patch, delete, and image/thumb serving.
/// </summary>
/// <remarks>
/// <para>
/// Called from <c>Program.cs</c> via <see cref="MapScreenshotEndpoints"/>. All routes require
/// a valid bearer token (<see cref="RequireAuthFilter"/>). Data is always scoped to the
/// authenticated user (except upload id-conflict checks, which look up by primary key only).
/// </para>
/// <para>
/// Upload size and JPEG quality are org settings
/// (<see cref="SettingKeys.ScreenshotMaxUploadBytes"/>,
/// <see cref="SettingKeys.ScreenshotJpegQuality"/>), not compile-time constants.
/// Thumbnail tiers are fixed (<see cref="ThumbTiers"/>).
/// </para>
/// <para>
/// Clients may supply <c>metadata.id</c> (typically the local spool item id) for idempotent
/// retries: a true duplicate (same user, machine, capture second, hash, size, monitor) returns
/// the existing row; a conflicting non-duplicate mints a new id. The response id is what the
/// client should store as <c>ServerScreenshotId</c>.
/// </para>
/// </remarks>
public static class ScreenshotEndpoints
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/jpg"
    };

    /// <summary>
    /// Registers screenshot routes under <c>/api/v1</c>.
    /// </summary>
    /// <param name="app">The endpoint route builder (usually the <see cref="WebApplication"/>).</param>
    /// <returns>The same <paramref name="app"/> for fluent chaining.</returns>
    /// <remarks>
    /// Antiforgery is disabled on POST upload because clients and the BFF send bearer tokens,
    /// not form cookies. Multipart size is also bounded by ASP.NET form options in <c>Program.cs</c>.
    /// </remarks>
    public static IEndpointRouteBuilder MapScreenshotEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireAuthFilter>();
        api.MapPost("/screenshots", UploadAsync).DisableAntiforgery();
        api.MapGet("/screenshots", ListAsync);
        api.MapGet("/screenshots/{id}", GetAsync);
        api.MapPatch("/screenshots/{id}", PatchAsync);
        api.MapDelete("/screenshots/{id}", DeleteAsync);
        api.MapPost("/screenshots/bulk-delete", BulkDeleteAsync);
        api.MapGet("/screenshots/pending", ListPendingAsync);
        api.MapPost("/screenshots/soft-delete", SoftDeleteAsync);
        api.MapPost("/screenshots/restore", RestoreAsync);
        api.MapPost("/screenshots/purge", PurgeAsync);
        api.MapPost("/screenshots/duplicates", FindDuplicatesAsync);
        api.MapPost("/screenshots/fingerprints/backfill", BackfillFingerprintsAsync);
        api.MapGet("/screenshots/{id}/image", GetImageAsync);
        api.MapGet("/screenshots/{id}/thumb", GetThumbAsync);
        return app;
    }

    /// <summary>
    /// Accepts a multipart screenshot (file + metadata JSON) and stores a JPEG original + thumbnail.
    /// </summary>
    /// <param name="request">HTTP request with multipart form (<c>file</c>, <c>metadata</c>).</param>
    /// <param name="principal">Authenticated user from bearer middleware.</param>
    /// <param name="shots">Screenshot insert repository.</param>
    /// <param name="machines">Validates <c>machineId</c> exists.</param>
    /// <param name="users">Loads the authenticated user for EXIF author fields.</param>
    /// <param name="blobs">Filesystem (or future object) blob store.</param>
    /// <param name="images">Decodes the upload, embeds EXIF/XMP, writes JPEG original + thumb.</param>
    /// <param name="settings">Org settings for max upload size and JPEG quality.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 201 with <see cref="ScreenshotResponse"/> on insert; 200 with existing row on idempotent
    /// retry of the same client id; 400/413/409 on validation or hard conflict.
    /// </returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Optional <c>metadata.id</c>: client may supply the intended screenshot id (spool id).</description></item>
    /// <item><description>True duplicates (same user + content signature) return the existing row without re-storing blobs.</description></item>
    /// <item><description>Id taken by a non-duplicate row → server mints a new id for this upload.</description></item>
    /// <item><description>Max file size: setting <see cref="SettingKeys.ScreenshotMaxUploadBytes"/>.</description></item>
    /// <item><description>Stored original is always JPEG with EXIF (viewer) and XMP (import).</description></item>
    /// <item><description>JPEG quality: setting <see cref="SettingKeys.ScreenshotJpegQuality"/>.</description></item>
    /// <item><description>Thumbs: one JPEG per <see cref="ThumbTiers"/> size under <c>thumbs/{size}/</c>.</description></item>
    /// <item><description>On DB insert failure, blob files are deleted (compensation).</description></item>
    /// <item><description>Optional <c>metadata.timestamp</c> supports manual/backfill uploads.</description></item>
    /// </list>
    /// </remarks>
    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        ScreenshotRepository shots,
        MachineRepository machines,
        UserRepository users,
        JournalRepository journals,
        ListRepository lists,
        IImageBlobStore blobs,
        ScreenshotImageProcessor images,
        SettingRepository settings,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "multipart/form-data required." });

        var maxBytes = await settings.GetLongAsync(
            SettingKeys.ScreenshotMaxUploadBytes,
            SettingKeys.Defaults.ScreenshotMaxUploadBytes,
            ct);

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "file part required." });
        if (file.Length > maxBytes)
            return Results.Json(
                new { error = "File too large.", maxBytes },
                statusCode: StatusCodes.Status413PayloadTooLarge);

        var contentType = file.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedTypes.Contains(contentType))
            return Results.BadRequest(new { error = "Unsupported content type (jpeg/png/webp)." });

        ScreenshotUploadMetadata meta;
        var metaField = form["metadata"].ToString();
        if (string.IsNullOrWhiteSpace(metaField))
            return Results.BadRequest(new { error = "metadata JSON field required." });
        try
        {
            meta = JsonSerializer.Deserialize<ScreenshotUploadMetadata>(metaField,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }.AddUuid7Converters())
                ?? throw new JsonException("null");
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid metadata JSON." });
        }

        if (meta.MachineId == Uuid7.Empty)
            return Results.BadRequest(new { error = "machineId required." });

        var machine = await machines.GetByIdAsync(meta.MachineId, ct);
        if (machine is null)
            return Results.BadRequest(new { error = "Unknown machineId." });

        if (meta.Id == WellKnownIds.None)
            return Results.BadRequest(new { error = "Nil id reserved." });

        var now = DateTimeOffset.UtcNow;
        // Whole-second resolution is all that matters for a screenshot's capture time.
        // Truncating here also guarantees that multiple screenshots from the same client
        // "batch" (e.g. a multi-monitor capture tick, which already shares one client-side
        // instant) end up with identical, clean timestamps rather than differing by ticks.
        var ts = TruncateToSeconds(meta.Timestamp ?? now);

        await using var imageStream = file.OpenReadStream();
        await using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        var incoming = ms.ToArray();

        if (!LooksLikeImage(incoming))
            return Results.BadRequest(new { error = "File does not look like an image." });

        // Day-boundary: close open journals that started on a previous local day and clone for today.
        // Remap client journal ids that still point at yesterday's open segment (needed in XMP).
        var dayRemap = await JournalDayBoundary.EnsureAsync(auth.Id, user.TimeZoneId, journals, ct);
        var journalEntryId = await JournalDayBoundary.RemapJournalIdAsync(
            auth.Id, meta.JournalEntryId, meta.MachineId, journals, dayRemap, ct);

        Uuid7? tagCustomerId = null;
        if (journalEntryId is { } jid)
        {
            var journal = await journals.GetForUserAsync(jid, auth.Id, ct);
            if (journal is not null)
            {
                var target = await lists.GetTargetAsync(journal.JournalTargetId, ct);
                tagCustomerId = target?.CustomerId;
            }
        }
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, meta.Tags, tagCustomerId, ct);
        var fingerprintJson = SerializeFingerprint(meta.Fingerprint);

        var requestedId = meta.Id;
        var id = requestedId ?? IdFactory.NewId();
        Screenshot? existingForId = requestedId is { } clientId
            ? await shots.GetByIdAsync(clientId, ct)
            : null;

        var jpegQuality = await settings.GetIntAsync(
            SettingKeys.ScreenshotJpegQuality,
            SettingKeys.Defaults.ScreenshotJpegQuality,
            ct);

        ScreenshotEncodedOriginal encoded;
        try
        {
            encoded = await EncodeStoredJpegAsync(
                images, ms, id, user, machine, meta, ts, tags, journalEntryId, fingerprintJson,
                jpegQuality, ct);
        }
        catch
        {
            return Results.BadRequest(new { error = "Could not decode image." });
        }

        var sha = Convert.ToHexString(SHA256.HashData(encoded.Jpeg)).ToLowerInvariant();

        if (existingForId is not null)
        {
            if (existingForId.UserId == auth.Id
                && IsLikelySameUpload(existingForId, meta, ts, sha, encoded.Jpeg.LongLength))
            {
                return Results.Ok(ToResponse(existingForId));
            }

            id = IdFactory.NewId();
            try
            {
                encoded = await EncodeStoredJpegAsync(
                    images, ms, id, user, machine, meta, ts, tags, journalEntryId, fingerprintJson,
                    jpegQuality, ct);
            }
            catch
            {
                return Results.BadRequest(new { error = "Could not decode image." });
            }
            sha = Convert.ToHexString(SHA256.HashData(encoded.Jpeg)).ToLowerInvariant();
        }

        const string storedType = "image/jpeg";
        var day = ts.UtcDateTime;
        var storageKey = $"{Uuid7Text.Format(auth.Id)}/{day:yyyy}/{day:MM}/{day:dd}/{Uuid7Text.Format(id)}.jpg";
        var thumbKey = ThumbTiers.KeyFor(storageKey, ThumbTiers.Largest);

        await using (var orig = new MemoryStream(encoded.Jpeg))
            await blobs.PutAsync(storageKey, orig, storedType, ct);
        foreach (var tier in encoded.Thumbs)
        {
            await using var thumb = new MemoryStream(tier.Jpeg);
            await blobs.PutAsync(ThumbTiers.KeyFor(storageKey, tier.Size), thumb, storedType, ct);
        }

        var shot = new Screenshot
        {
            Id = id,
            UserId = auth.Id,
            MachineId = meta.MachineId,
            TimestampUtc = ts.ToUniversalTime(),
            ClientTimeZoneId = meta.ClientTimeZoneId,
            ReceivedAtUtc = now,
            MonitorId = meta.MonitorId,
            MonitorName = meta.MonitorName,
            WindowTitle = meta.WindowTitle,
            ProgramName = meta.ProgramName,
            CommandLine = meta.CommandLine,
            ContentType = storedType,
            ByteSize = encoded.Jpeg.LongLength,
            Width = encoded.Width,
            Height = encoded.Height,
            Sha256 = sha,
            StorageKey = storageKey,
            ThumbStorageKey = thumbKey,
            Notes = meta.Notes,
            Tags = string.IsNullOrEmpty(tags) ? null : tags,
            JournalEntryId = journalEntryId,
            CreatedAt = now,
            Fingerprint = fingerprintJson
        };

        try
        {
            await shots.InsertAsync(shot, ct);
        }
        catch
        {
            // Race: another insert won the same id between GetById and Insert.
            await blobs.DeleteAsync(storageKey, ct);
            await DeleteThumbsAsync(blobs, storageKey, thumbKey, ct);
            if (requestedId is { } raceId)
            {
                var raced = await shots.GetForUserAsync(raceId, auth.Id, ct);
                if (raced is not null && IsLikelySameUpload(raced, meta, ts, sha, encoded.Jpeg.LongLength))
                    return Results.Ok(ToResponse(raced));
            }
            return Results.Conflict(new { error = "Could not insert screenshot (duplicate id?)." });
        }

        return Results.Created($"/api/v1/screenshots/{Uuid7Text.Format(id)}", ToResponse(shot));
    }

    /// <summary>
    /// Whether an existing row looks like a retry of the same client upload (same machine,
    /// capture second, stored-JPEG hash, and optional monitor identity).
    /// </summary>
    private static bool IsLikelySameUpload(
        Screenshot existing,
        ScreenshotUploadMetadata meta,
        DateTimeOffset ts,
        string sha,
        long byteSize)
    {
        if (existing.MachineId != meta.MachineId)
            return false;
        if (TruncateToSeconds(existing.TimestampUtc) != TruncateToSeconds(ts))
            return false;
        if (!string.Equals(existing.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            return false;
        if (existing.ByteSize != byteSize)
            return false;

        // Monitor labels: match when both sides have a value.
        if (!string.IsNullOrWhiteSpace(meta.MonitorId)
            && !string.IsNullOrWhiteSpace(existing.MonitorId)
            && !string.Equals(meta.MonitorId, existing.MonitorId, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrWhiteSpace(meta.MonitorName)
            && !string.IsNullOrWhiteSpace(existing.MonitorName)
            && !string.Equals(meta.MonitorName, existing.MonitorName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Lists the caller's screenshots for a day or UTC range.
    /// </summary>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="users">Loads profile timezone for day queries.</param>
    /// <param name="shots">Screenshot query repository.</param>
    /// <param name="date">Optional local calendar date <c>yyyy-MM-dd</c> in the user's timezone.</param>
    /// <param name="from">Optional range start (UTC) when not using <paramref name="date"/>.</param>
    /// <param name="to">Optional range end (UTC, exclusive) when not using <paramref name="date"/>.</param>
    /// <param name="monitor">Optional monitor name filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with a list of <see cref="ScreenshotResponse"/> (empty if none).</returns>
    /// <remarks>
    /// If neither date nor from/to is provided, defaults to "today" in the user's profile timezone.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        UserRepository users,
        ScreenshotRepository shots,
        string? date,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? monitor,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null) return Results.Unauthorized();

        DateTimeOffset fromUtc, toUtc;
        if (!string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var d))
        {
            (fromUtc, toUtc) = DayRange.ForLocalDate(d, user.TimeZoneId);
        }
        else if (from is not null && to is not null)
        {
            fromUtc = from.Value.ToUniversalTime();
            toUtc = to.Value.ToUniversalTime();
        }
        else
        {
            var tz = SafeTz(user.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var today = DateOnly.FromDateTime(localNow.DateTime);
            (fromUtc, toUtc) = DayRange.ForLocalDate(today, user.TimeZoneId);
        }

        var list = await shots.ListForUserRangeAsync(auth.Id, fromUtc, toUtc, monitor, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    /// <summary>
    /// Returns metadata for one screenshot owned by the caller.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with metadata, or 404 if missing or not owned.</returns>
    private static async Task<IResult> GetAsync(
        Uuid7Param id, ClaimsPrincipal principal, ScreenshotRepository shots, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var shot = await shots.GetForUserAsync(id, auth.Id, ct);
        return shot is null ? Results.NotFound() : Results.Ok(ToResponse(shot));
    }

    /// <summary>
    /// Updates notes and/or tags on a screenshot owned by the caller.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="body">
    /// Fields to set. <c>null</c> Notes/Tags keep the existing value; empty string clears.
    /// </param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with updated metadata, or 404.</returns>
    private static async Task<IResult> PatchAsync(
        Uuid7Param id, UpdateScreenshotRequest body, ClaimsPrincipal principal,
        ScreenshotRepository shots, JournalRepository journals, ListRepository lists, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var shot = await shots.GetForUserAsync(id, auth.Id, ct);
        if (shot is null) return Results.NotFound();
        // null = leave unchanged (partial update); "" = clear.
        var notes = body.Notes is null ? shot.Notes : body.Notes;
        string? tags = shot.Tags;
        if (body.Tags is not null)
        {
            Uuid7? journalId = body.SetJournalEntryId ? body.JournalEntryId : shot.JournalEntryId;
            Uuid7? tagCustomerId = null;
            if (journalId is { } jid)
            {
                var journal = await journals.GetForUserAsync(jid, auth.Id, ct);
                if (journal is not null)
                {
                    var target = await lists.GetTargetAsync(journal.JournalTargetId, ct);
                    tagCustomerId = target?.CustomerId;
                }
            }
            tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, tagCustomerId, ct);
            if (string.IsNullOrEmpty(tags)) tags = null;
        }
        await shots.UpdateMetadataAsync(
            id, auth.Id, notes, tags,
            body.SetJournalEntryId, body.JournalEntryId, ct);
        shot = (await shots.GetForUserAsync(id, auth.Id, ct))!;
        return Results.Ok(ToResponse(shot));
    }

    /// <summary>
    /// Deletes a screenshot row and its original + thumbnail blobs.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="blobs">Blob store for file removal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 on success, 404 if not found/owned.</returns>
    private static async Task<IResult> DeleteAsync(
        Uuid7Param id, ClaimsPrincipal principal, ScreenshotRepository shots,
        IImageBlobStore blobs, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var shot = await shots.GetForUserAsync(id, auth.Id, ct);
        if (shot is null) return Results.NotFound();
        await shots.DeleteForUserAsync(id, auth.Id, ct);
        await blobs.DeleteAsync(shot.StorageKey, ct);
        await DeleteThumbsAsync(blobs, shot.StorageKey, shot.ThumbStorageKey, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Deletes several screenshots owned by the caller in one call (gallery bulk-select).
    /// </summary>
    /// <param name="body">Ids to delete; ids not found or not owned by the caller are skipped.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="blobs">Blob store for file removal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with the count actually deleted.</returns>
    private static async Task<IResult> BulkDeleteAsync(
        ScreenshotBulkDeleteRequest body, ClaimsPrincipal principal, ScreenshotRepository shots,
        IImageBlobStore blobs, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var deleted = 0;
        foreach (var id in body.Ids.Distinct())
        {
            var shot = await shots.GetForUserAsync(id, auth.Id, ct);
            if (shot is null) continue;
            await shots.DeleteForUserAsync(id, auth.Id, ct);
            await blobs.DeleteAsync(shot.StorageKey, ct);
            await DeleteThumbsAsync(blobs, shot.StorageKey, shot.ThumbStorageKey, ct);
            deleted++;
        }
        return Results.Ok(new ScreenshotBulkDeleteResponse { Deleted = deleted });
    }

    /// <summary>Lists screenshots the caller has soft-deleted, newest delete first.</summary>
    private static async Task<IResult> ListPendingAsync(
        ClaimsPrincipal principal, ScreenshotRepository shots, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var list = await shots.ListPendingDeletesAsync(auth.Id, ct);
        return Results.Ok(list.Select(ToResponse));
    }

    /// <summary>Hides owned screenshots from the gallery without removing files.</summary>
    private static async Task<IResult> SoftDeleteAsync(
        ScreenshotBulkDeleteRequest body, ClaimsPrincipal principal, ScreenshotRepository shots, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var count = await shots.SoftDeleteForUserAsync(auth.Id, body.Ids ?? new List<Uuid7>(), ct);
        return Results.Ok(new ScreenshotBulkMutationResponse { Count = count });
    }

    /// <summary>Returns owned pending screenshots to the gallery.</summary>
    private static async Task<IResult> RestoreAsync(
        ScreenshotBulkDeleteRequest body, ClaimsPrincipal principal, ScreenshotRepository shots, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var count = await shots.RestoreForUserAsync(auth.Id, body.Ids ?? new List<Uuid7>(), ct);
        return Results.Ok(new ScreenshotBulkMutationResponse { Count = count });
    }

    /// <summary>
    /// Permanently deletes screenshots that are already pending. Active rows are skipped.
    /// Removes the row and its original and thumbnail blobs.
    /// </summary>
    private static async Task<IResult> PurgeAsync(
        ScreenshotBulkDeleteRequest body, ClaimsPrincipal principal, ScreenshotRepository shots,
        IImageBlobStore blobs, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var purged = 0;
        foreach (var id in (body.Ids ?? new List<Uuid7>()).Distinct())
        {
            var shot = await shots.GetForUserAsync(id, auth.Id, ct);
            if (shot?.DeletedAt is null)
                continue;
            if (!await shots.PurgePendingForUserAsync(id, auth.Id, ct))
                continue;
            await blobs.DeleteAsync(shot.StorageKey, ct);
            await DeleteThumbsAsync(blobs, shot.StorageKey, shot.ThumbStorageKey, ct);
            purged++;
        }

        return Results.Ok(new ScreenshotBulkMutationResponse { Count = purged });
    }

    /// <summary>
    /// Backfills missing fingerprints for the caller's screenshots in a day or UTC range,
    /// then returns ids among <paramref name="body"/>.Ids that are blank or idle-duplicates.
    /// </summary>
    private static async Task<IResult> FindDuplicatesAsync(
        ScreenshotDuplicateRequest body,
        ClaimsPrincipal principal,
        ScreenshotRepository shots,
        IImageBlobStore blobs,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var ids = (body.Ids ?? new List<Uuid7>())
            .Where(id => id != Uuid7.Empty)
            .Distinct()
            .Take(2000)
            .ToList();
        if (ids.Count == 0)
        {
            return Results.Ok(new ScreenshotDuplicateResponse());
        }

        var maxDirty = GridLayout.ClampMaxDirtyCells(body.MaxDirtyCells, GridLayout.MinCols, GridLayout.MinRows);
        // Allow up to 20 even on 16x8 when the caller is scanning mixed sizes; clamp 1-20.
        if (body.MaxDirtyCells < 1)
        {
            maxDirty = 1;
        }
        else if (body.MaxDirtyCells > 20)
        {
            maxDirty = 20;
        }
        else
        {
            maxDirty = body.MaxDirtyCells;
        }

        var rows = await shots.ListForUserByIdsAsync(auth.Id, ids, ct);
        var (backfilled, failed) = await ScreenshotFingerprintService.EnsureFingerprintsAsync(
            rows, auth.Id, shots, blobs, ct);

        var scanItems = new List<DuplicateScan.Item>(rows.Count);
        foreach (var shot in rows)
        {
            var fp = ScreenshotFingerprintService.TryToFingerprint(shot);
            scanItems.Add(new DuplicateScan.Item(
                ScreenshotFingerprintService.SeriesKey(shot),
                shot.TimestampUtc,
                fp,
                scanItems.Count));
        }

        var marked = DuplicateScan.SelectDiscardIndexes(scanItems, maxDirty);
        var dupIds = marked.Select(i => rows[i].Id).ToList();
        return Results.Ok(new ScreenshotDuplicateResponse
        {
            DuplicateIds = dupIds,
            Backfilled = backfilled,
            FingerprintFailed = failed
        });
    }

    /// <summary>Computes fingerprints for owned screenshots that do not have one yet.</summary>
    private static async Task<IResult> BackfillFingerprintsAsync(
        ScreenshotFingerprintBackfillRequest body,
        ClaimsPrincipal principal,
        UserRepository users,
        ScreenshotRepository shots,
        IImageBlobStore blobs,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var user = await users.GetByIdAsync(auth.Id, ct);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        DateTimeOffset fromUtc, toUtc;
        if (!string.IsNullOrWhiteSpace(body.Date) && DateOnly.TryParse(body.Date, out var d))
        {
            (fromUtc, toUtc) = DayRange.ForLocalDate(d, user.TimeZoneId);
        }
        else if (body.From is not null && body.To is not null)
        {
            fromUtc = body.From.Value.ToUniversalTime();
            toUtc = body.To.Value.ToUniversalTime();
        }
        else
        {
            var tz = SafeTz(user.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var today = DateOnly.FromDateTime(localNow.DateTime);
            (fromUtc, toUtc) = DayRange.ForLocalDate(today, user.TimeZoneId);
        }

        var limit = body.Limit < 1 ? 100 : (body.Limit > 500 ? 500 : body.Limit);
        var missing = await shots.ListMissingFingerprintAsync(auth.Id, fromUtc, toUtc, limit, ct);
        var (processed, failed) = await ScreenshotFingerprintService.EnsureFingerprintsAsync(
            missing, auth.Id, shots, blobs, ct);
        var remaining = await shots.CountMissingFingerprintAsync(auth.Id, fromUtc, toUtc, ct);
        return Results.Ok(new ScreenshotFingerprintBackfillResponse
        {
            Processed = processed,
            Failed = failed,
            Remaining = remaining
        });
    }

    /// <summary>
    /// Streams the original image bytes for a screenshot owned by the caller.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="blobs">Blob store.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 file stream, or 404.</returns>
    private static async Task<IResult> GetImageAsync(
        Uuid7Param id, ClaimsPrincipal principal, ScreenshotRepository shots,
        IImageBlobStore blobs, HttpContext http, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var shot = await shots.GetForUserAsync(id, auth.Id, ct);
        if (shot is null) return Results.NotFound();
        return await BlobFileAsync(blobs, http, shot.StorageKey, shot.ContentType, ct);
    }

    /// <summary>
    /// Streams the JPEG thumbnail for a screenshot owned by the caller.
    /// </summary>
    /// <param name="id">Screenshot id.</param>
    /// <param name="principal">Authenticated user.</param>
    /// <param name="shots">Screenshot repository.</param>
    /// <param name="blobs">Blob store.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 JPEG stream, or 404.</returns>
    private static async Task<IResult> GetThumbAsync(
        Uuid7Param id, int? size, ClaimsPrincipal principal, ScreenshotRepository shots,
        IImageBlobStore blobs, HttpContext http, CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var shot = await shots.GetForUserAsync(id, auth.Id, ct);
        if (shot is null) return Results.NotFound();

        var want = ThumbTiers.Snap(size ?? ThumbTiers.Largest);
        var key = ThumbTiers.KeyFor(shot.StorageKey, want);
        if (await blobs.StatAsync(key, ct) is not null)
            return await BlobFileAsync(blobs, http, key, "image/jpeg", ct);

        // Tier not generated yet (run `schnapp-utils thumbs`): serve the next larger tier or the legacy thumb,
        // and force revalidation so the right tier is picked up once it exists.
        foreach (var tier in ThumbTiers.Sizes.Where(s => s > want))
        {
            var larger = ThumbTiers.KeyFor(shot.StorageKey, tier);
            if (await blobs.StatAsync(larger, ct) is not null)
                return await BlobFileAsync(blobs, http, larger, "image/jpeg", ct, "private, no-cache");
        }
        return await BlobFileAsync(blobs, http, shot.ThumbStorageKey, "image/jpeg", ct, "private, no-cache");
    }

    // Removes every tier plus the legacy/largest thumb key stored on the row.
    private static async Task DeleteThumbsAsync(
        IImageBlobStore blobs, string storageKey, string thumbStorageKey, CancellationToken ct)
    {
        foreach (var size in ThumbTiers.Sizes)
            await blobs.DeleteAsync(ThumbTiers.KeyFor(storageKey, size), ct);
        if (!string.IsNullOrWhiteSpace(thumbStorageKey)
            && thumbStorageKey != ThumbTiers.KeyFor(storageKey, ThumbTiers.Largest))
            await blobs.DeleteAsync(thumbStorageKey, ct);
    }

    // ETag from size+mtime (keys can be overwritten in place by re-encode); File result answers 304 and ranges.
    private static async Task<IResult> BlobFileAsync(
        IImageBlobStore blobs, HttpContext http, string key, string contentType, CancellationToken ct,
        string cacheControl = "private, max-age=86400")
    {
        var stat = await blobs.StatAsync(key, ct);
        if (stat is null) return Results.NotFound();
        var stream = await blobs.OpenReadAsync(key, ct);
        if (stream is null) return Results.NotFound();
        http.Response.Headers.CacheControl = cacheControl;
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
            $"\"{stat.Length:x}-{stat.LastModifiedUtc.UtcTicks:x}\"");
        return Results.File(stream, contentType, lastModified: stat.LastModifiedUtc, entityTag: etag,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Maps a domain entity to the public API DTO (no storage keys or hashes).
    /// </summary>
    /// <param name="s">Persisted screenshot.</param>
    /// <returns>Safe response for JSON serialization.</returns>
    private static ScreenshotResponse ToResponse(Screenshot s) => new()
    {
        Id = s.Id,
        UserId = s.UserId,
        MachineId = s.MachineId,
        TimestampUtc = s.TimestampUtc,
        ClientTimeZoneId = s.ClientTimeZoneId,
        ReceivedAtUtc = s.ReceivedAtUtc,
        MonitorId = s.MonitorId,
        MonitorName = s.MonitorName,
        WindowTitle = s.WindowTitle,
        ProgramName = s.ProgramName,
        ContentType = s.ContentType,
        ByteSize = s.ByteSize,
        Width = s.Width,
        Height = s.Height,
        Notes = s.Notes,
        Tags = s.Tags,
        JournalEntryId = s.JournalEntryId,
        CreatedAt = s.CreatedAt,
        DeletedAt = s.DeletedAt,
        Fingerprint = DeserializeFingerprint(s.Fingerprint)
    };

    private static readonly JsonSerializerOptions FingerprintJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string? SerializeFingerprint(ScreenshotFingerprint? fp)
        => fp is null ? null : JsonSerializer.Serialize(fp, FingerprintJson);

    private static ScreenshotFingerprint? DeserializeFingerprint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScreenshotFingerprint>(json, FingerprintJson);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ScreenshotEncodedOriginal> EncodeStoredJpegAsync(
        ScreenshotImageProcessor images,
        MemoryStream source,
        Uuid7 id,
        User user,
        Machine machine,
        ScreenshotUploadMetadata meta,
        DateTimeOffset ts,
        string tags,
        Uuid7? journalEntryId,
        string? fingerprintJson,
        int jpegQuality,
        CancellationToken ct)
    {
        source.Position = 0;
        var ctx = new ScreenshotEmbedContext
        {
            ScreenshotId = id,
            UserId = user.Id,
            UserCode = user.Code,
            UserEmail = user.Email,
            UserName = user.Name,
            MachineId = machine.Id,
            MachineCode = machine.Code,
            MachineName = machine.Name,
            TimestampUtc = ts.ToUniversalTime(),
            ClientTimeZoneId = meta.ClientTimeZoneId,
            MonitorId = meta.MonitorId,
            MonitorName = meta.MonitorName,
            WindowTitle = meta.WindowTitle,
            ProgramName = meta.ProgramName,
            CommandLine = meta.CommandLine,
            Notes = meta.Notes,
            Tags = string.IsNullOrEmpty(tags) ? null : tags,
            JournalEntryId = journalEntryId,
            FingerprintJson = fingerprintJson
        };
        return await images.EncodeAsync(source, ctx, jpegQuality, ct);
    }

    /// <summary>
    /// Lightweight magic-byte check for JPEG/PNG/WebP before ImageSharp decode.
    /// </summary>
    /// <param name="bytes">File content.</param>
    /// <returns><see langword="true"/> if the header looks like a supported image.</returns>
    private static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 4) return false;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') return true;
        return false;
    }

    /// <summary>
    /// Resolves an IANA timezone id, falling back to UTC if unknown on this host.
    /// </summary>
    /// <param name="id">Timezone id from the user profile.</param>
    /// <returns>A usable <see cref="TimeZoneInfo"/>.</returns>
    private static TimeZoneInfo SafeTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// Drops sub-second precision from a timestamp, preserving its offset.
    /// </summary>
    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Offset);

    /// <summary>
    /// Drops sub-quarter-second precision from a timestamp, preserving its offset.
    /// </summary>
    /// <param name="value">The timestamp to truncate.</param>
    /// <returns>The timestamp truncated to the nearest quarter second.</returns>
    private static DateTimeOffset TruncateToQuarterSecond(DateTimeOffset value)
    {
        // 250 ms = 2,500,000 ticks (1 tick = 100 ns)
        const long QuarterSecondTicks = 2_500_000;

        long ticks = value.Ticks;
        long flooredTicks = (ticks / QuarterSecondTicks) * QuarterSecondTicks;

        return new DateTimeOffset(flooredTicks, value.Offset);
    }
}
