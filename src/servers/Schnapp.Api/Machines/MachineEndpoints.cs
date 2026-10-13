using System.Security.Claims;
using Schnapp.Api.Auth;
using Schnapp.Api.Tags;
using Schnapp.Contracts;
using Schnapp.Data.Repositories;
using Schnapp.Domain;
using Schnapp.Domain.Entities;

using Medo;
namespace Schnapp.Api.Machines;

/// <summary>
/// Minimal API routes for machine registration, listing, find, and link.
/// </summary>
/// <remarks>
/// Called from <c>Program.cs</c>. All routes require a valid bearer token
/// (<see cref="RequireAuthFilter"/>).
/// </remarks>
public static class MachineEndpoints
{
    /// <summary>
    /// Registers machine endpoints under <c>/api/v1</c>.
    /// </summary>
    /// <param name="app">Route builder.</param>
    /// <returns><paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapMachineEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireAuthFilter>();
        api.MapGet("/machines", ListAsync);
        api.MapPost("/machines", CreateAsync);
        api.MapPost("/machines/find", FindAsync);
        api.MapGet("/machines/{id}", GetAsync);
        api.MapPatch("/machines/{id}", UpdateAsync);
        api.MapPost("/machines/{id}/link", LinkAsync);

        var admin = app.MapGroup("/api/v1").AddEndpointFilter<RequireAdminFilter>();
        admin.MapGet("/admin/machines", ListAllAsync);
        admin.MapPost("/admin/machines/bulk-status", BulkStatusAsync);
        return app;
    }

    private static async Task<IResult> BulkStatusAsync(
        BulkMachineStatusRequest body,
        MachineRepository machines,
        CancellationToken ct)
    {
        if (body.Ids is null || body.Ids.Count == 0)
            return Results.BadRequest(new { error = "Select at least one machine." });
        if (body.Status is not (0 or 1))
            return Results.BadRequest(new { error = "Status must be 0 (Active) or 1 (Disabled)." });

        var updated = await machines.UpdateStatusBulkAsync(body.Ids, body.Status, ct);
        return Results.Ok(new BulkMachineStatusResponse { Updated = updated });
    }

    /// <summary>
    /// Admin: lists every machine org-wide, one row per owning user, with the owner's
    /// name/email attached.
    /// </summary>
    private static async Task<IResult> ListAllAsync(MachineRepository machines, CancellationToken ct)
    {
        var rows = await machines.ListAllWithOwnersAsync(ct);
        var list = rows.Select(r => new AdminMachineResponse
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Fqdn = r.Fqdn,
            HardwareFingerprint = r.HardwareFingerprint,
            LastSeen = r.LastSeen,
            Status = r.Status,
            ColourCode = r.ColourCode,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            UserId = r.UserId,
            UserName = r.UserName,
            UserEmail = r.UserEmail,
            Notes = r.Notes,
            Tags = r.Tags
        }).ToList();
        return Results.Ok(list);
    }

    /// <summary>Gets one machine if the caller is linked to it, or is an admin.</summary>
    private static async Task<IResult> GetAsync(
        Uuid7Param id,
        ClaimsPrincipal principal,
        MachineRepository machines,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var machine = await machines.GetByIdAsync(id, ct);
        if (machine is null) return Results.NotFound();

        var link = await machines.GetLinkAsync(auth.Id, id, ct);
        if (link is null && !auth.IsAdmin)
            return Results.NotFound();

        link ??= new UserMachine
        {
            UserId = auth.Id,
            MachineId = id,
            RegisteredAt = machine.CreatedAt,
            Status = 0,
            CreatedAt = machine.CreatedAt,
            UpdatedAt = machine.UpdatedAt
        };
        return Results.Ok(ToResponse(machine, link));
    }

    /// <summary>Updates machine fields (and link display name when linked).</summary>
    private static async Task<IResult> UpdateAsync(
        Uuid7Param id,
        UpdateMachineRequest body,
        ClaimsPrincipal principal,
        MachineRepository machines,
        ListRepository lists,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Code))
            return Results.BadRequest(new { error = "Code and name are required." });

        var auth = AuthUser.FromPrincipal(principal);
        var machine = await machines.GetByIdAsync(id, ct);
        if (machine is null) return Results.NotFound();

        var link = await machines.GetLinkAsync(auth.Id, id, ct);
        if (link is null && !auth.IsAdmin)
            return Results.Forbid();

        machine.Code = body.Code.Trim();
        machine.Name = body.Name.Trim();
        machine.Fqdn = NullIfEmpty(body.Fqdn);
        machine.HardwareFingerprint = NullIfEmpty(body.HardwareFingerprint);
        machine.Notes = body.Notes;
        var tags = await TagCollation.NormalizeAndEnsureRequiredAsync(lists, body.Tags, customerId: null, ct);
        machine.Tags = string.IsNullOrEmpty(tags) ? null : tags;
        machine.Status = body.Status;
        machine.ColourCode = body.ColourCode;
        machine.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await machines.UpdateAsync(machine, ct);
        }
        catch (Exception)
        {
            return Results.Conflict(new { error = "Machine code or name already exists." });
        }

        if (link is not null)
        {
            link.DisplayName = NullIfEmpty(body.DisplayName);
            link.UpdatedAt = DateTimeOffset.UtcNow;
            await machines.UpdateLinkAsync(link, ct);
        }
        else
        {
            link = new UserMachine
            {
                UserId = auth.Id,
                MachineId = id,
                RegisteredAt = machine.CreatedAt,
                Status = 0,
                CreatedAt = machine.CreatedAt,
                UpdatedAt = machine.UpdatedAt
            };
        }

        return Results.Ok(ToResponse(machine, link));
    }

    /// <summary>
    /// Lists machines linked to the current user.
    /// </summary>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        MachineRepository machines,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var rows = await machines.ListForUserAsync(auth.Id, ct);
        var list = rows.Select(r => ToResponse(r.Machine, r.Link)).ToList();
        return Results.Ok(list);
    }

    /// <summary>
    /// Creates a new machine and links it to the current user.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateMachineRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        MachineRepository machines,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        var auth = AuthUser.FromPrincipal(principal);
        var now = DateTimeOffset.UtcNow;
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? DeriveCode(request.Name)
            : request.Code.Trim();

        var machine = new Machine
        {
            Id = IdFactory.NewId(),
            Code = code,
            Name = request.Name.Trim(),
            Fqdn = NullIfEmpty(request.Fqdn),
            HardwareFingerprint = NullIfEmpty(request.HardwareFingerprint),
            LastIpAddresses = IpJson(http),
            Status = request.Status,
            ColourCode = request.ColourCode,
            CreatedAt = now,
            UpdatedAt = now,
            LastSeen = now
        };

        var link = new UserMachine
        {
            UserId = auth.Id,
            MachineId = machine.Id,
            RegisteredAt = now,
            DisplayName = NullIfEmpty(request.DisplayName),
            Status = 0,
            CreatedAt = now,
            UpdatedAt = now,
            LastSeen = now,
            LastUpdated = now
        };

        try
        {
            await machines.InsertWithUserLinkAsync(machine, link, ct);
        }
        catch (Exception)
        {
            return Results.Conflict(new { error = "Machine code or name already exists." });
        }

        return Results.Created($"/api/v1/machines/{Uuid7Text.Format(machine.Id)}", ToResponse(machine, link));
    }

    /// <summary>
    /// Finds machines matching fingerprint and/or FQDN (read-only search).
    /// </summary>
    /// <remarks>
    /// Does not auto-bind; client must call link or create. Confidence is heuristic only.
    /// </remarks>
    private static async Task<IResult> FindAsync(
        FindMachinesRequest request,
        ClaimsPrincipal principal,
        MachineRepository machines,
        CancellationToken ct)
    {
        _ = AuthUser.FromPrincipal(principal);
        var fqdn = NullIfEmpty(request.Fqdn);
        var fp = NullIfEmpty(request.HardwareFingerprint);
        if (fqdn is null && fp is null)
            return Results.BadRequest(new { error = "Provide fqdn and/or hardwareFingerprint." });

        var found = await machines.FindCandidatesAsync(fqdn, fp, ct);
        var candidates = found.Select(m =>
        {
            var conf = "low";
            var reasons = new List<string>();
            if (fp is not null && string.Equals(m.HardwareFingerprint, fp, StringComparison.Ordinal))
            {
                conf = "high";
                reasons.Add("fingerprint");
            }
            if (fqdn is not null && string.Equals(m.Fqdn, fqdn, StringComparison.OrdinalIgnoreCase))
            {
                if (conf != "high") conf = "medium";
                reasons.Add("fqdn");
            }
            return new MachineFindCandidate
            {
                Id = m.Id,
                Name = m.Name,
                Code = m.Code,
                Fqdn = m.Fqdn,
                HardwareFingerprint = m.HardwareFingerprint,
                Confidence = conf,
                Reason = string.Join("+", reasons)
            };
        })
        .OrderByDescending(c => c.Confidence switch { "high" => 3, "medium" => 2, _ => 1 })
        .ToList();

        return Results.Ok(new FindMachinesResponse { Candidates = candidates });
    }

    /// <summary>
    /// Links the current user to an existing machine and refreshes identity fields.
    /// </summary>
    private static async Task<IResult> LinkAsync(
        Uuid7Param id,
        CreateMachineRequest? body,
        ClaimsPrincipal principal,
        HttpContext http,
        MachineRepository machines,
        CancellationToken ct)
    {
        var auth = AuthUser.FromPrincipal(principal);
        var machine = await machines.GetByIdAsync(id, ct);
        if (machine is null)
            return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        await machines.UpdateIdentityAsync(
            id,
            NullIfEmpty(body?.Fqdn),
            NullIfEmpty(body?.HardwareFingerprint),
            IpJson(http),
            now,
            ct);

        var link = new UserMachine
        {
            UserId = auth.Id,
            MachineId = id,
            RegisteredAt = now,
            DisplayName = NullIfEmpty(body?.DisplayName),
            Status = body?.Status ?? 0,
            CreatedAt = now,
            UpdatedAt = now,
            LastSeen = now,
            LastUpdated = now
        };
        await machines.UpsertUserLinkAsync(link, ct);

        machine = (await machines.GetByIdAsync(id, ct))!;
        return Results.Ok(ToResponse(machine, link));
    }

    private static MachineResponse ToResponse(Machine m, UserMachine link) => new()
    {
        Id = m.Id,
        Code = m.Code,
        Name = m.Name,
        Fqdn = m.Fqdn,
        HardwareFingerprint = m.HardwareFingerprint,
        LastIpAddresses = m.LastIpAddresses,
        RegisteredAt = link.RegisteredAt,
        DisplayName = link.DisplayName,
        LastSeen = m.LastSeen,
        Status = m.Status,
        ColourCode = m.ColourCode,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        LinkStatus = link.Status,
        Notes = m.Notes,
        Tags = m.Tags
    };

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? IpJson(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        return ip is null ? null : $"[\"{ip}\"]";
    }

    private static string DeriveCode(string name)
    {
        var cleaned = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "machine";
        var baseCode = cleaned.Length > 24 ? cleaned[..24] : cleaned;
        return baseCode + "-" + Guid.NewGuid().ToString("N")[..6];
    }
}
