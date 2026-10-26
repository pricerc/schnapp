using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Schnapp.Contracts;

using Medo;
namespace Schnapp.Web.Services;

/// <summary>
/// Server-side HTTP client for the Schnapp API (BFF). Attaches the bearer token from the auth cookie.
/// </summary>
/// <remarks>
/// Called from Razor Page handlers. Expected base address comes from <c>Schnapp:ApiBaseUrl</c>.
/// </remarks>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }.AddUuid7Converters();

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Creates an API client using the configured <see cref="HttpClient"/> and current request context.
    /// </summary>
    /// <param name="http">Named/typed client with <see cref="HttpClient.BaseAddress"/> set to the API.</param>
    /// <param name="httpContextAccessor">Used to read the encrypted auth cookie token for this browser session.</param>
    public ApiClient(HttpClient http, IHttpContextAccessor httpContextAccessor)
    {
        _http = http;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Logs in with email/code/name and optional password; returns plaintext token once.
    /// </summary>
    /// <param name="identity">Unique email, code, or name.</param>
    /// <param name="password">Plaintext password, or null/empty for passwordless accounts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login payload, or null if credentials were rejected.</returns>
    public async Task<LoginResponse?> LoginAsync(string identity, string? password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Login = identity, Password = password },
            ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct);
    }

    /// <summary>
    /// Loads the current user profile using the session bearer token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Profile, or null if unauthenticated / API returned 401.</returns>
    public async Task<UserProfileResponse?> GetMeAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me");
        if (request is null)
            return null;

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct);
    }

    /// <summary>
    /// Updates the current user profile via <c>PATCH /api/v1/me</c>.
    /// </summary>
    /// <param name="body">Partial profile update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated profile and null error on success; null profile and the API's error body otherwise.</returns>
    public async Task<(UserProfileResponse? Profile, string? Error)> UpdateMeAsync(UpdateProfileRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, "/api/v1/me");
        if (request is null)
            return (null, "Not signed in.");

        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (null, null);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));

        return (await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct), null);
    }

    /// <summary>
    /// Admin: creates a new user via the API.
    /// </summary>
    /// <param name="body">Create-user payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created profile, or an error message string.</returns>
    public async Task<(UserProfileResponse? Profile, string? Error)> CreateUserAsync(
        CreateUserRequest body,
        CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/admin/users");
        if (request is null)
            return (null, "Not signed in.");

        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (null, "Not signed in.");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return (null, "Admin role required.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return (null, "User already exists (email, code, or name).");
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));

        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct);
        return (profile, null);
    }

    /// <summary>
    /// Admin: lists all user profiles.
    /// </summary>
    public async Task<(List<UserProfileResponse>? Users, string? Error)> ListUsersAsync(
        CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/admin/users");
        if (request is null)
            return (null, "Not signed in.");

        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (null, "Not signed in.");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return (null, "Admin role required.");
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));

        var users = await response.Content.ReadFromJsonAsync<List<UserProfileResponse>>(JsonOptions, ct);
        return (users ?? [], null);
    }

    /// <summary>Admin: get one user by id.</summary>
    public async Task<(UserProfileResponse? User, string? Error)> GetUserAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/admin/users/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "User not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return (null, "Admin role required.");
        if (!response.IsSuccessStatusCode) return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct), null);
    }

    /// <summary>Admin: update a user.</summary>
    public async Task<(UserProfileResponse? User, string? Error)> UpdateUserAsync(
        Uuid7 id, AdminUpdateUserRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/admin/users/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return (null, "Admin role required.");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "User not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return (null, "User with that email, code, or name already exists.");
        if (!response.IsSuccessStatusCode) return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct), null);
    }

    /// <summary>Admin: delete a user.</summary>
    public async Task<string?> DeleteUserAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/v1/admin/users/{Uuid7Text.Format(id)}");
        if (request is null) return "Not signed in.";
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return "Admin role required.";
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return "User not found.";
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Probes API health (no auth).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task<HealthResponse?> GetHealthAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HealthResponse>("/api/v1/health", JsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Lists machines for the signed-in user.</summary>
    public async Task<List<MachineResponse>?> ListMachinesAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/machines");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<MachineResponse>>(JsonOptions, ct);
    }

    /// <summary>Registers a new machine for the signed-in user.</summary>
    public async Task<(MachineResponse? Machine, string? Error)> CreateMachineAsync(
        CreateMachineRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/machines");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct), null);
    }

    /// <summary>Finds machines by fingerprint/FQDN.</summary>
    public async Task<(FindMachinesResponse? Result, string? Error)> FindMachinesAsync(
        FindMachinesRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/machines/find");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<FindMachinesResponse>(JsonOptions, ct), null);
    }

    /// <summary>Links an existing machine to the signed-in user.</summary>
    public async Task<(MachineResponse? Machine, string? Error)> LinkMachineAsync(
        Uuid7 id, CreateMachineRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/api/v1/machines/{Uuid7Text.Format(id)}/link");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct), null);
    }

    /// <summary>Gets one machine (own link or admin).</summary>
    public async Task<(MachineResponse? Machine, string? Error)> GetMachineAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/machines/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "Machine not found.");
        if (!response.IsSuccessStatusCode) return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct), null);
    }

    /// <summary>Updates a machine (own link or admin).</summary>
    public async Task<(MachineResponse? Machine, string? Error)> UpdateMachineAsync(
        Uuid7 id, UpdateMachineRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/machines/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "Machine not found.");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return (null, "Not allowed to edit this machine.");
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return (null, "Machine code or name already exists.");
        if (!response.IsSuccessStatusCode) return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct), null);
    }

    /// <summary>Admin: lists every machine org-wide with the owning user's identity.</summary>
    public async Task<List<AdminMachineResponse>?> ListAllMachinesAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/admin/machines");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<AdminMachineResponse>>(JsonOptions, ct);
    }

    /// <summary>Admin: set status on many machines.</summary>
    public async Task<(int Updated, string? Error)> BulkUpdateMachineStatusAsync(
        IEnumerable<Uuid7> ids, int status, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/admin/machines/bulk-status");
        if (request is null) return (0, "Not signed in.");
        request.Content = JsonContent.Create(new BulkMachineStatusRequest
        {
            Ids = ids.ToList(),
            Status = status
        }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return (0, "Admin role required.");
        if (!response.IsSuccessStatusCode) return (0, await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<BulkMachineStatusResponse>(JsonOptions, ct);
        return (body?.Updated ?? 0, null);
    }

    /// <summary>Lists org list items (customers|projects|tasks).</summary>
    public async Task<List<ListItemResponse>?> ListItemsAsync(string kind, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/lists/{kind}");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ListItemResponse>>(JsonOptions, ct);
    }

    /// <summary>Lists journal targets (customer/project/task triples).</summary>
    public async Task<List<JournalTargetResponse>?> ListJournalTargetsAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/lists/journal-targets");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<JournalTargetResponse>>(JsonOptions, ct);
    }

    /// <summary>Get-or-create a journal target by customer/project/task.</summary>
    public async Task<(JournalTargetResponse? Target, string? Error)> GetOrCreateJournalTargetAsync(
        JournalTargetRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/lists/journal-targets");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JournalTargetResponse>(JsonOptions, ct), null);
    }

    /// <summary>Patches journal target code/name/notes/tags.</summary>
    public async Task<(JournalTargetResponse? Target, string? Error)> UpdateJournalTargetAsync(
        Uuid7 id, UpdateJournalTargetRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/lists/journal-targets/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JournalTargetResponse>(JsonOptions, ct), null);
    }

    /// <summary>
    /// Lists tag dictionary entries.
    /// </summary>
    /// <param name="customerId">Optional customer filter.</param>
    /// <param name="scope">
    /// <c>global</c>, <c>customer</c>, or null for default (all / global+customer for autocomplete).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<TagResponse>?> ListTagsAsync(
        Uuid7? customerId = null, string? scope = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (customerId is { } cid && cid != Uuid7.Empty)
            qs.Add("customerId=" + Uri.EscapeDataString(Uuid7Text.Format(cid)));
        if (!string.IsNullOrWhiteSpace(scope))
            qs.Add("scope=" + Uri.EscapeDataString(scope));
        var path = "/api/v1/lists/tags" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path);
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<TagResponse>>(JsonOptions, ct);
    }

    /// <summary>Creates a dictionary tag (global or customer-scoped).</summary>
    public async Task<(TagResponse? Tag, string? Error)> CreateTagAsync(TagRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/lists/tags");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<TagResponse>(JsonOptions, ct), null);
    }

    /// <summary>Updates notes on a dictionary tag.</summary>
    public async Task<(TagResponse? Tag, string? Error)> UpdateTagAsync(UpdateTagRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, "/api/v1/lists/tags");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Tag not found.");
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<TagResponse>(JsonOptions, ct), null);
    }

    /// <summary>Deletes a dictionary tag row.</summary>
    public async Task<string?> DeleteTagAsync(string tag, Uuid7? customerId = null, CancellationToken ct = default)
    {
        var qs = "tag=" + Uri.EscapeDataString(tag);
        if (customerId is { } cid && cid != Uuid7.Empty)
            qs += "&customerId=" + Uri.EscapeDataString(Uuid7Text.Format(cid));
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, "/api/v1/lists/tags?" + qs);
        if (request is null) return "Not signed in.";
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return "Tag not found.";
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Moves a global tag onto a customer (removes from general list).</summary>
    public async Task<(TagResponse? Tag, string? Error)> AssignTagToCustomerAsync(
        string tag, Uuid7 customerId, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/lists/tags/assign-customer");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(new ReassignTagRequest { Tag = tag, CustomerId = customerId }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Global tag not found.");
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<TagResponse>(JsonOptions, ct), null);
    }

    /// <summary>Moves a customer tag to the general (global) list.</summary>
    public async Task<(TagResponse? Tag, string? Error)> MakeTagGlobalAsync(
        string tag, Uuid7 customerId, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/lists/tags/make-global");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(new ReassignTagRequest { Tag = tag, CustomerId = customerId }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Customer tag not found.");
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<TagResponse>(JsonOptions, ct), null);
    }

    /// <summary>Creates a list item.</summary>
    public async Task<(ListItemResponse? Item, string? Error)> CreateListItemAsync(
        string kind, ListItemRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/api/v1/lists/{kind}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<ListItemResponse>(JsonOptions, ct), null);
    }

    /// <summary>Updates a list item via PUT.</summary>
    public async Task<(ListItemResponse? Item, string? Error)> UpdateListItemAsync(
        string kind, Uuid7 id, ListItemRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"/api/v1/lists/{kind}/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<ListItemResponse>(JsonOptions, ct), null);
    }

    /// <summary>Deletes a list item.</summary>
    public async Task<string?> DeleteListItemAsync(string kind, Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/v1/lists/{kind}/{Uuid7Text.Format(id)}");
        if (request is null) return "Not signed in.";
        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Admin: set a user's password.</summary>
    public async Task<string?> ResetUserPasswordAsync(Uuid7 id, string password, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/api/v1/admin/users/{Uuid7Text.Format(id)}/reset-password");
        if (request is null) return "Not signed in.";
        request.Content = JsonContent.Create(new ResetPasswordRequest { Password = password }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return "Admin role required.";
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Lists org settings.</summary>
    public async Task<List<SettingResponse>?> ListSettingsAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/settings");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<SettingResponse>>(JsonOptions, ct);
    }

    /// <summary>Admin: update a setting value.</summary>
    public async Task<string?> UpdateSettingAsync(string key, string value, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/settings/{Uri.EscapeDataString(key)}");
        if (request is null) return "Not signed in.";
        request.Content = JsonContent.Create(new UpdateSettingRequest { Value = value }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Lists screenshots for a local calendar date (user timezone on server).</summary>
    /// <param name="date">yyyy-MM-dd in the user's profile timezone.</param>
    /// <param name="monitor">Optional monitor name filter.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<ScreenshotResponse>?> ListScreenshotsAsync(
        string date, string? monitor = null, CancellationToken ct = default)
    {
        var qs = $"date={Uri.EscapeDataString(date)}";
        if (!string.IsNullOrWhiteSpace(monitor))
            qs += $"&monitor={Uri.EscapeDataString(monitor)}";
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/screenshots?{qs}");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ScreenshotResponse>>(JsonOptions, ct);
    }

    /// <summary>
    /// Product manual screenshot upload (un-monitored system / manual grab).
    /// </summary>
    /// <param name="machineId">Machine to attribute the shot to.</param>
    /// <param name="file">Image stream.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="contentType">MIME type.</param>
    /// <param name="monitorName">Optional monitor label.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="timestamp">Capture time (UTC or with offset); null means server/now.</param>
    /// <param name="clientTimeZoneId">IANA zone for the capture (display/day grouping).</param>
    /// <param name="journalEntryId">Optional open journal to stamp on the shot.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<(ScreenshotResponse? Shot, string? Error)> UploadScreenshotAsync(
        Uuid7 machineId,
        Stream file,
        string fileName,
        string contentType,
        string? monitorName,
        string? notes,
        DateTimeOffset? timestamp = null,
        string? clientTimeZoneId = null,
        Uuid7? journalEntryId = null,
        CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/screenshots");
        if (request is null) return (null, "Not signed in.");

        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(file);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        var meta = JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machineId,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            ClientTimeZoneId = clientTimeZoneId ?? "UTC",
            MonitorName = monitorName,
            Notes = notes,
            JournalEntryId = journalEntryId
        });
        content.Add(new StringContent(meta), "metadata");
        request.Content = content;

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonOptions, ct), null);
    }

    /// <summary>Gets one screenshot metadata by id.</summary>
    public async Task<ScreenshotResponse?> GetScreenshotAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/screenshots/{Uuid7Text.Format(id)}");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonOptions, ct);
    }

    /// <summary>Patches screenshot notes/tags.</summary>
    public async Task<(ScreenshotResponse? Shot, string? Error)> UpdateScreenshotAsync(
        Uuid7 id, UpdateScreenshotRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/screenshots/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonOptions, ct), null);
    }

    /// <summary>Lists screenshots the user has soft-deleted, newest delete first.</summary>
    public async Task<List<ScreenshotResponse>?> ListPendingScreenshotsAsync(CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/screenshots/pending");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ScreenshotResponse>>(JsonOptions, ct);
    }

    /// <summary>Hides screenshots from the gallery without removing files.</summary>
    public async Task<(int Count, string? Error)> SoftDeleteScreenshotsAsync(
        IEnumerable<Uuid7> ids, CancellationToken ct = default)
        => await PostScreenshotIdsAsync("/api/v1/screenshots/soft-delete", ids, ct);

    /// <summary>Returns soft-deleted screenshots to the gallery.</summary>
    public async Task<(int Count, string? Error)> RestoreScreenshotsAsync(
        IEnumerable<Uuid7> ids, CancellationToken ct = default)
        => await PostScreenshotIdsAsync("/api/v1/screenshots/restore", ids, ct);

    /// <summary>Permanently deletes screenshots that are already pending, including their files.</summary>
    public async Task<(int Count, string? Error)> PurgeScreenshotsAsync(
        IEnumerable<Uuid7> ids, CancellationToken ct = default)
        => await PostScreenshotIdsAsync("/api/v1/screenshots/purge", ids, ct);

    private async Task<(int Count, string? Error)> PostScreenshotIdsAsync(
        string path, IEnumerable<Uuid7> ids, CancellationToken ct)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, path);
        if (request is null) return (0, "Not signed in.");
        request.Content = JsonContent.Create(new ScreenshotBulkDeleteRequest { Ids = ids.ToList() }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (0, await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<ScreenshotBulkMutationResponse>(JsonOptions, ct);
        return (body?.Count ?? 0, null);
    }

    /// <summary>Deletes a screenshot owned by the current user.</summary>
    public async Task<string?> DeleteScreenshotAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/v1/screenshots/{Uuid7Text.Format(id)}");
        if (request is null) return "Not signed in.";
        var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Deletes several screenshots owned by the current user in one call.</summary>
    /// <returns>Number actually deleted, and null error on success; 0 and the API's error body otherwise.</returns>
    public async Task<(int Deleted, string? Error)> DeleteScreenshotsAsync(IEnumerable<Uuid7> ids, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/screenshots/bulk-delete");
        if (request is null) return (0, "Not signed in.");
        request.Content = JsonContent.Create(new ScreenshotBulkDeleteRequest { Ids = ids.ToList() }, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (0, await response.Content.ReadAsStringAsync(ct));
        var body = await response.Content.ReadFromJsonAsync<ScreenshotBulkDeleteResponse>(JsonOptions, ct);
        return (body?.Deleted ?? 0, null);
    }

    /// <summary>Backfills missing fingerprints then returns idle/blank duplicate ids among <paramref name="ids"/>.</summary>
    public async Task<ScreenshotDuplicateResponse?> FindScreenshotDuplicatesAsync(
        IEnumerable<Uuid7> ids, int maxDirtyCells, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/screenshots/duplicates");
        if (request is null)
        {
            return null;
        }

        request.Content = JsonContent.Create(
            new ScreenshotDuplicateRequest { Ids = ids.ToList(), MaxDirtyCells = maxDirtyCells },
            options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ScreenshotDuplicateResponse>(JsonOptions, ct);
    }

    /// <summary>Computes fingerprints for screenshots in a local date or UTC range that lack one.</summary>
    public async Task<ScreenshotFingerprintBackfillResponse?> BackfillScreenshotFingerprintsAsync(
        ScreenshotFingerprintBackfillRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/screenshots/fingerprints/backfill");
        if (request is null)
        {
            return null;
        }

        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ScreenshotFingerprintBackfillResponse>(JsonOptions, ct);
    }

    /// <summary>Absolute API URL for a screenshot image or thumb (for img src via BFF proxy pages).</summary>
    public string ApiPath(string relative) => relative.TrimStart('/');

    /// <summary>
    /// Lists journal entries for the signed-in user.
    /// When <paramref name="from"/>/<paramref name="to"/> or C/P/T filters are set, filtering is
    /// applied in the API/DB (not client-side).
    /// </summary>
    public async Task<List<JournalEntryResponse>?> ListJournalsAsync(
        CancellationToken ct = default,
        DateOnly? from = null,
        DateOnly? to = null,
        TimeSummaryFilterQuery? filters = null,
        int? limit = null)
    {
        var qs = new List<string>();
        if (from is { } f)
            qs.Add("fromDate=" + Uri.EscapeDataString(f.ToString("yyyy-MM-dd")));
        if (to is { } t)
            qs.Add("toDate=" + Uri.EscapeDataString(t.ToString("yyyy-MM-dd")));
        if (limit is > 0)
            qs.Add("limit=" + limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var path = "/api/v1/journals";
        if (qs.Count > 0 || (filters is not null && filters.ToQueryString().Length > 0))
        {
            var q = string.Join("&", qs);
            if (filters is not null)
            {
                var fqs = filters.ToQueryString().TrimStart('&');
                if (fqs.Length > 0)
                    q = string.IsNullOrEmpty(q) ? fqs : q + "&" + fqs;
            }
            if (!string.IsNullOrEmpty(q))
                path += "?" + q;
        }
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path);
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<JournalEntryResponse>>(JsonOptions, ct);
    }

    /// <summary>
    /// Loads a time-spent summary for the signed-in user over an inclusive local date range.
    /// </summary>
    public async Task<TimeSummaryResponse?> GetTimeSummaryAsync(
        DateOnly from,
        DateOnly to,
        TimeSummaryFilterQuery? filters = null,
        CancellationToken ct = default)
    {
        var qs =
            $"fromDate={Uri.EscapeDataString(from.ToString("yyyy-MM-dd"))}" +
            $"&toDate={Uri.EscapeDataString(to.ToString("yyyy-MM-dd"))}";
        if (filters is not null)
            qs += filters.ToQueryString();
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"/api/v1/reports/time-summary?{qs}");
        if (request is null) return null;
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TimeSummaryResponse>(JsonOptions, ct);
    }


    /// <summary>Starts a journal segment on a machine.</summary>
    public async Task<(JournalEntryResponse? Entry, string? Error)> StartJournalAsync(
        StartJournalRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/journals/start");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct), null);
    }

    /// <summary>Stops an open journal entry.</summary>
    public async Task<(JournalEntryResponse? Entry, string? Error)> StopJournalAsync(
        Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, $"/api/v1/journals/{Uuid7Text.Format(id)}/stop");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(new StopJournalRequest(), options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct), null);
    }

    /// <summary>Patches journal notes/tags/times.</summary>
    public async Task<(JournalEntryResponse? Entry, string? Error)> UpdateJournalAsync(
        Uuid7 id, UpdateJournalRequest body, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Patch, $"/api/v1/journals/{Uuid7Text.Format(id)}");
        if (request is null) return (null, "Not signed in.");
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (null, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct), null);
    }

    /// <summary>Deletes a journal entry owned by the signed-in user.</summary>
    public async Task<string?> DeleteJournalAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/v1/journals/{Uuid7Text.Format(id)}");
        if (request is null) return "Not signed in.";
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Journal not found.";
        if (!response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(ct);
        return null;
    }

    private HttpRequestMessage? CreateAuthorizedRequest(HttpMethod method, string path)
    {
        var token = AuthCookie.GetToken(_httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
