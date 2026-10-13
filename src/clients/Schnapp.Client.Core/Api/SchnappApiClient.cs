namespace Schnapp.Client.Core.Api;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Schnapp.Client.Core.Config;
using Schnapp.Contracts;

using Medo;

/// <summary>
/// HTTP client for the Schnapp REST API used by CLI and tray apps.
/// </summary>
/// <remarks>
/// Construct with a base URL and optional token from <see cref="ClientSettingsStore"/>.
/// netstandard2.0-safe surface.
/// </remarks>
public sealed class SchnappApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }.AddUuid7Converters();

    private readonly HttpClient _http;
    private string _token;

    /// <summary>
    /// Creates an API client.
    /// </summary>
    /// <param name="baseUrl">API root, e.g. http://127.0.0.1:5080</param>
    /// <param name="token">Optional bearer token.</param>
    /// <param name="http">Optional injected HttpClient (tests).</param>
    public SchnappApiClient(string baseUrl, string? token = null, HttpClient? http = null)
    {
        // Private Schnapp servers are typically LAN/localhost; system HTTP_PROXY (e.g. Squid)
        // must not intercept 127.0.0.1 — that makes the proxy dial itself and fail.
        if (http != null)
        {
            _http = http;
        }
        else
        {
            var handler = new HttpClientHandler { UseProxy = false };
            _http = new HttpClient(handler);
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }

        _token = token ?? "";
    }

    /// <summary>Updates the bearer token used for subsequent calls.</summary>
    public void SetToken(string token) => _token = token;

    /// <summary>
    /// POST /api/v1/auth/login — identity is email, code, or name; password optional.
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string identity, string? password = null, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest { Login = identity, Password = password },
            JsonOptions,
            ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct).ConfigureAwait(false);
        if (body == null)
        {
            throw new InvalidOperationException("Empty login response.");
        }

        _token = body.Token;
        return body;
    }

    /// <summary>GET /api/v1/me</summary>
    public async Task<UserProfileResponse> GetMeAsync(CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/v1/me");
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty profile.");
    }

    /// <summary>GET /api/v1/machines</summary>
    public async Task<IReadOnlyList<MachineResponse>> ListMachinesAsync(CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/v1/machines");
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<MachineResponse>>(JsonOptions, ct).ConfigureAwait(false) ?? [];
    }

    /// <summary>POST /api/v1/machines</summary>
    public async Task<MachineResponse> RegisterMachineAsync(CreateMachineRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/machines");
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty machine response.");
    }

    /// <summary>POST /api/v1/machines/find</summary>
    public async Task<FindMachinesResponse> FindMachinesAsync(FindMachinesRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/machines/find");
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<FindMachinesResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? new FindMachinesResponse();
    }

    /// <summary>POST /api/v1/machines/{id}/link</summary>
    public async Task<MachineResponse> LinkMachineAsync(Uuid7 id, CreateMachineRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/machines/" + Uuid7Text.Format(id) + "/link");
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<MachineResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty link response.");
    }

    /// <summary>POST /api/v1/journals/start</summary>
    public async Task<JournalEntryResponse> StartJournalAsync(StartJournalRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/journals/start");
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty journal response.");
    }

    /// <summary>POST /api/v1/journals/{id}/stop</summary>
    public async Task<JournalEntryResponse> StopJournalAsync(Uuid7 id, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/journals/" + Uuid7Text.Format(id) + "/stop");
        req.Content = JsonContent.Create(new StopJournalRequest(), options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty journal response.");
    }

    /// <summary>GET /api/v1/journals</summary>
    public async Task<IReadOnlyList<JournalEntryResponse>> ListJournalsAsync(CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/v1/journals");
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<JournalEntryResponse>>(JsonOptions, ct).ConfigureAwait(false)
               ?? [];
    }

    /// <summary>PATCH /api/v1/journals/{id}</summary>
    public async Task<JournalEntryResponse> UpdateJournalAsync(Uuid7 id, UpdateJournalRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(new HttpMethod("PATCH"), "api/v1/journals/" + Uuid7Text.Format(id));
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JournalEntryResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty journal response.");
    }

    /// <summary>GET /api/v1/lists/{kind} (customers|projects|tasks)</summary>
    public async Task<IReadOnlyList<ListItemResponse>> ListItemsAsync(string kind, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/v1/lists/" + kind);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<ListItemResponse>>(JsonOptions, ct).ConfigureAwait(false)
               ?? [];
    }

    /// <summary>POST /api/v1/lists/{kind}</summary>
    public async Task<ListItemResponse> CreateListItemAsync(string kind, ListItemRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/lists/" + kind);
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ListItemResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty list create response.");
    }

    /// <summary>GET /api/v1/lists/journal-targets</summary>
    public async Task<IReadOnlyList<JournalTargetResponse>> ListJournalTargetsAsync(CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Get, "api/v1/lists/journal-targets");
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<JournalTargetResponse>>(JsonOptions, ct).ConfigureAwait(false)
               ?? [];
    }

    /// <summary>
    /// GET /api/v1/lists/tags — dictionary for autocomplete.
    /// </summary>
    /// <param name="customerId">When set, returns global + that customer's tags.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<TagResponse>> ListTagsAsync(Uuid7? customerId = null, CancellationToken ct = default)
    {
        var path = customerId is { } cid && cid != Uuid7.Empty
            ? "api/v1/lists/tags?customerId=" + Uri.EscapeDataString(Uuid7Text.Format(cid))
            : "api/v1/lists/tags";
        using var req = CreateAuth(HttpMethod.Get, path);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<TagResponse>>(JsonOptions, ct).ConfigureAwait(false)
               ?? [];
    }

    /// <summary>POST /api/v1/lists/journal-targets (get-or-create)</summary>
    public async Task<JournalTargetResponse> GetOrCreateJournalTargetAsync(JournalTargetRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/lists/journal-targets");
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JournalTargetResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty journal target response.");
    }

    /// <summary>POST multipart /api/v1/screenshots</summary>
    public async Task<ScreenshotResponse> UploadScreenshotAsync(
        Stream file,
        string fileName,
        string contentType,
        ScreenshotUploadMetadata metadata,
        CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/screenshots");
        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(file);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        content.Add(streamContent, "file", fileName ?? "upload.bin");
        var metaJson = JsonSerializer.Serialize(metadata, JsonOptions);
        content.Add(new StringContent(metaJson, Encoding.UTF8, "application/json"), "metadata");
        req.Content = content;
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty upload response.");
    }

    /// <summary>PATCH /api/v1/screenshots/{id} — notes, tags, and/or journal link.</summary>
    public async Task<ScreenshotResponse> UpdateScreenshotAsync(
        Uuid7 id, UpdateScreenshotRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(new HttpMethod("PATCH"), "api/v1/screenshots/" + Uuid7Text.Format(id));
        req.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Empty screenshot update response.");
    }

    /// <summary>POST /api/v1/screenshots/fingerprints/backfill</summary>
    public async Task<ScreenshotFingerprintBackfillResponse> BackfillScreenshotFingerprintsAsync(
        ScreenshotFingerprintBackfillRequest body, CancellationToken ct = default)
    {
        using var req = CreateAuth(HttpMethod.Post, "api/v1/screenshots/fingerprints/backfill");
        req.Content = JsonContent.Create(body ?? new ScreenshotFingerprintBackfillRequest(), options: JsonOptions);
        var response = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<ScreenshotFingerprintBackfillResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty fingerprint backfill response.");
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private HttpRequestMessage CreateAuth(HttpMethod method, string relative)
    {
        var req = new HttpRequestMessage(method, relative);
        if (!string.IsNullOrEmpty(_token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        return req;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new ApiException((int)response.StatusCode, body);
    }
}

/// <summary>Non-success API response.</summary>
public sealed class ApiException : Exception
{
    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Response body text.</summary>
    public string ResponseBody { get; }

    /// <summary>Creates an API exception.</summary>
    public ApiException(int statusCode, string responseBody)
        : base("API " + statusCode + ": " + responseBody)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody ?? "";
    }
}
