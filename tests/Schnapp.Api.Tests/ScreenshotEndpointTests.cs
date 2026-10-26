using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Schnapp.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Testcontainers.PostgreSql;
using Xunit;

using Medo;
namespace Schnapp.Api.Tests;

/// <summary>
/// Integration tests for screenshot upload, listing, thumbnails, and user isolation.
/// </summary>
/// <remarks>
/// <para>
/// Isolated temp DB via <see cref="PostgresTestSupport"/>. Also uses a unique temp
/// <c>Schnapp:BlobRoot</c> directory so blob files do not collide across runs.
/// </para>
/// <para>
/// Dispose drops the DB and best-effort deletes the blob directory. If the process is killed
/// mid-run, both <c>schnapp_t_*</c> DBs and <c>/tmp/schnapp-blobs-*</c> dirs can leak
/// (DB: <c>./scripts/cleanup-test-dbs.sh</c>).
/// </para>
/// </remarks>
public class ScreenshotEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";
    private string? _createdDatabase;
    private string? _baseCs;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string _adminEmail = "";
    private string _blobRoot = "";
    private const string AdminPassword = "AdminPass123!";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _baseCs = Environment.GetEnvironmentVariable("SCHNAPP_TEST_CONNECTION");
        var (cs, container, createdDb) = await PostgresTestSupport.CreateMigratedDatabaseAsync();
        _connectionString = cs;
        _container = container;
        _createdDatabase = createdDb;
        _blobRoot = Path.Combine(Path.GetTempPath(), "schnapp-blobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_blobRoot);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _adminEmail = $"admin-{suffix}@test.local";
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Main", _connectionString);
            b.UseSetting("Schnapp:MigrateOnStartup", "false");
            b.UseSetting("Schnapp:AdminEmail", _adminEmail);
            b.UseSetting("Schnapp:AdminPassword", AdminPassword);
            b.UseSetting("Schnapp:BlobRoot", _blobRoot);
        });
        _client = _factory.CreateClient();
        await LoginAdminAsync();
    }

    /// <summary>
    /// Disposes factory (releases DB pools), drops temp DB, then deletes blob root (best-effort).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        if (_baseCs is not null && _createdDatabase is not null)
            await PostgresTestSupport.DropDatabaseIfCreatedAsync(_baseCs, _createdDatabase);
        try { if (Directory.Exists(_blobRoot)) Directory.Delete(_blobRoot, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Upload_list_thumb_and_user_isolation()
    {
        var machine = await CreateMachineAsync("ShotBox");
        var png = MakePngBytes(80, 60);

        var upload = await UploadAsync(machine.Id, png, "image/png", "Mon-1");
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var shot = await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        shot!.Width.Should().Be(80);
        shot.Height.Should().Be(60);
        shot.ContentType.Should().Be("image/jpeg");

        var list = await _client!.GetFromJsonAsync<List<ScreenshotResponse>>(
            $"/api/v1/screenshots?date={DateTime.UtcNow:yyyy-MM-dd}", JsonTest.Options);
        list.Should().Contain(s => s.Id == shot.Id);

        var thumb = await _client!.GetAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot.Id)}/thumb");
        thumb.StatusCode.Should().Be(HttpStatusCode.OK);
        thumb.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");

        // second user cannot see
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await _client!.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!",
            Name = $"User {suffix}",
            Code = $"u{suffix}"
        });
        _client!.DefaultRequestHeaders.Authorization = null;
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = $"u-{suffix}@test.local",
            Password = "UserPass123!"
        });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options))!.Token;
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var forbidden = await _client!.GetAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot.Id)}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Thumb_size_selects_pre_generated_tier_and_falls_back_when_missing()
    {
        var machine = await CreateMachineAsync("TierBox");
        var upload = await UploadAsync(machine.Id, MakePngBytes(1000, 600), "image/png", "Mon-1");
        var shot = await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        var url = $"/api/v1/screenshots/{Uuid7Text.Format(shot!.Id)}/thumb";

        foreach (var size in new[] { 128, 256, 384, 512, 640 })
        {
            var resp = await _client!.GetAsync($"{url}?size={size}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var img = await Image.LoadAsync(new MemoryStream(await resp.Content.ReadAsByteArrayAsync()));
            img.Width.Should().Be(size);
        }

        // Odd sizes snap up to the next tier; no size means the largest.
        using (var snapped = await Image.LoadAsync(new MemoryStream(
                   await _client!.GetByteArrayAsync($"{url}?size=200"))))
            snapped.Width.Should().Be(256);
        using (var largest = await Image.LoadAsync(new MemoryStream(await _client!.GetByteArrayAsync(url))))
            largest.Width.Should().Be(640);

        // Missing tier file: serve the next larger tier, uncached.
        var tierFile = Directory.EnumerateFiles(Path.Combine(_blobRoot, "thumbs", "128"), "*.jpg",
            SearchOption.AllDirectories).Single();
        File.Delete(tierFile);
        var fallback = await _client!.GetAsync($"{url}?size=128");
        fallback.StatusCode.Should().Be(HttpStatusCode.OK);
        fallback.Headers.CacheControl!.NoCache.Should().BeTrue();
        using var fb = await Image.LoadAsync(new MemoryStream(await fallback.Content.ReadAsByteArrayAsync()));
        fb.Width.Should().Be(256);

        // Delete removes every tier.
        var del = await _client!.DeleteAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot.Id)}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Directory.EnumerateFiles(Path.Combine(_blobRoot, "thumbs"), "*.jpg", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Thumb_and_image_support_conditional_get()
    {
        var machine = await CreateMachineAsync("CacheBox");
        var upload = await UploadAsync(machine.Id, MakePngBytes(40, 30), "image/png", "Mon-1");
        var shot = await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);

        foreach (var kind in new[] { "thumb", "image" })
        {
            var url = $"/api/v1/screenshots/{Uuid7Text.Format(shot!.Id)}/{kind}";
            var first = await _client!.GetAsync(url);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            first.Headers.CacheControl!.Private.Should().BeTrue();
            first.Headers.ETag.Should().NotBeNull();

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.IfNoneMatch.Add(first.Headers.ETag!);
            var second = await _client.SendAsync(req);
            second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        }
    }

    [Fact]
    public async Task Upload_reencodes_to_jpeg_with_exif_and_xmp()
    {
        var machine = await CreateMachineAsync("MetaBox");
        var when = new DateTimeOffset(2026, 4, 1, 9, 15, 0, TimeSpan.Zero);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(MakePngBytes(32, 24));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "meta.png");
        content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machine.Id,
            Timestamp = when,
            ClientTimeZoneId = "UTC",
            MonitorId = "mon-2",
            MonitorName = "Office",
            WindowTitle = "Editor",
            ProgramName = "code",
            Notes = "desk",
            Tags = "alpha beta"
        }, JsonTest.Options)), "metadata");

        var upload = await _client!.PostAsync("/api/v1/screenshots", content);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var shot = await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        shot!.ContentType.Should().Be("image/jpeg");

        var imageResp = await _client.GetAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot.Id)}/image");
        imageResp.StatusCode.Should().Be(HttpStatusCode.OK);
        imageResp.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");

        var jpegBytes = await imageResp.Content.ReadAsByteArrayAsync();
        jpegBytes.AsSpan().IndexOf("ASCII\0\0\0"u8).Should().BeGreaterThan(0, "UserComment uses the EXIF ASCII 8-byte ID");

        using var decoded = await Image.LoadAsync(new MemoryStream(jpegBytes));
        decoded.Width.Should().Be(32);
        decoded.Height.Should().Be(24);

        var exif = decoded.Metadata.ExifProfile;
        exif.Should().NotBeNull();
        ExifString(exif!, ExifTag.Make).Should().Contain("MetaBox");
        ExifString(exif!, ExifTag.Model).Should().Be("Office (mon-2)");
        ExifString(exif!, ExifTag.DateTimeOriginal).Should().Be("2026:04:01 09:15:00");
        ExifString(exif!, ExifTag.OffsetTimeOriginal).Should().Be("+00:00");
        ExifString(exif!, ExifTag.ImageDescription).Should().Be("Editor");
        ExifString(exif!, ExifTag.Software).Should().Be("Schnapp");
        exif!.TryGetValue(ExifTag.UserComment, out var comment).Should().BeTrue();
        comment!.Value.Code.Should().Be(EncodedString.CharacterCode.ASCII);
        comment.Value.Text.Should().Contain("MetaBox");
        comment.Value.Text.Should().Contain("Office (mon-2)");
        comment.Value.Text.Should().Contain("code");

        decoded.Metadata.XmpProfile.Should().NotBeNull();
        var xmp = decoded.Metadata.XmpProfile!.ToXDocument().ToString();
        xmp.Should().Contain("schnapp.price.org.nz/ns/1");
        xmp.Should().Contain(shot.Id.ToString());
        xmp.Should().Contain(machine.Id.ToString());
        xmp.Should().Contain("Office");
        xmp.Should().Contain("alpha");
        xmp.Should().Contain("desk");
    }

    [Fact]
    public async Task Delete_removes_row_and_files()
    {
        var machine = await CreateMachineAsync("DelBox");
        var upload = await UploadAsync(machine.Id, MakePngBytes(40, 40), "image/png", null);
        upload.EnsureSuccessStatusCode();
        var shot = await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);

        var del = await _client!.DeleteAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot!.Id)}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await _client.GetAsync($"/api/v1/screenshots/{Uuid7Text.Format(shot.Id)}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Soft_delete_hides_from_gallery_until_restore_or_purge()
    {
        await LoginAdminAsync();
        var machine = await CreateMachineAsync("SoftBox");
        var upload = await UploadAsync(machine.Id, MakePngBytes(32, 32), "image/png", "Mon-1");
        upload.EnsureSuccessStatusCode();
        var shot = (await upload.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options))!;
        var idText = Uuid7Text.Format(shot.Id);
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var soft = await _client!.PostAsJsonAsync("/api/v1/screenshots/soft-delete", new ScreenshotBulkDeleteRequest
        {
            Ids = new List<Uuid7> { shot.Id }
        }, JsonTest.Options);
        soft.StatusCode.Should().Be(HttpStatusCode.OK);
        (await soft.Content.ReadFromJsonAsync<ScreenshotBulkMutationResponse>(JsonTest.Options))!
            .Count.Should().Be(1);

        var gallery = await _client.GetFromJsonAsync<List<ScreenshotResponse>>(
            $"/api/v1/screenshots?date={day}", JsonTest.Options);
        gallery.Should().NotContain(s => s.Id == shot.Id);

        var pending = await _client.GetFromJsonAsync<List<ScreenshotResponse>>(
            "/api/v1/screenshots/pending", JsonTest.Options);
        pending.Should().ContainSingle(s => s.Id == shot.Id);
        pending!.Single(s => s.Id == shot.Id).DeletedAt.Should().NotBeNull();

        var stillThere = await _client.GetAsync($"/api/v1/screenshots/{idText}/image");
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);

        var restore = await _client.PostAsJsonAsync("/api/v1/screenshots/restore", new ScreenshotBulkDeleteRequest
        {
            Ids = new List<Uuid7> { shot.Id }
        }, JsonTest.Options);
        restore.EnsureSuccessStatusCode();
        var back = await _client.GetFromJsonAsync<List<ScreenshotResponse>>(
            $"/api/v1/screenshots?date={day}", JsonTest.Options);
        back.Should().Contain(s => s.Id == shot.Id);

        (await _client.PostAsJsonAsync("/api/v1/screenshots/soft-delete", new ScreenshotBulkDeleteRequest
        {
            Ids = new List<Uuid7> { shot.Id }
        }, JsonTest.Options)).EnsureSuccessStatusCode();

        var purge = await _client.PostAsJsonAsync("/api/v1/screenshots/purge", new ScreenshotBulkDeleteRequest
        {
            Ids = new List<Uuid7> { shot.Id }
        }, JsonTest.Options);
        purge.EnsureSuccessStatusCode();
        (await purge.Content.ReadFromJsonAsync<ScreenshotBulkMutationResponse>(JsonTest.Options))!
            .Count.Should().Be(1);
        (await _client.GetAsync($"/api/v1/screenshots/{idText}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var live = await UploadAsync(machine.Id, MakePngBytes(16, 16), "image/png", "Mon-1");
        live.EnsureSuccessStatusCode();
        var liveShot = (await live.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options))!;
        var skipped = await _client.PostAsJsonAsync("/api/v1/screenshots/purge", new ScreenshotBulkDeleteRequest
        {
            Ids = new List<Uuid7> { liveShot.Id }
        }, JsonTest.Options);
        skipped.EnsureSuccessStatusCode();
        (await skipped.Content.ReadFromJsonAsync<ScreenshotBulkMutationResponse>(JsonTest.Options))!
            .Count.Should().Be(0);
        (await _client.GetAsync($"/api/v1/screenshots/{Uuid7Text.Format(liveShot.Id)}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rejects_non_image()
    {
        var machine = await CreateMachineAsync("BadBox");
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
        }, "file", "x.txt");
        content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machine.Id
        })), "metadata");
        var response = await _client!.PostAsync("/api/v1/screenshots", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_respects_client_timestamp()
    {
        var machine = await CreateMachineAsync("TimeBox");
        var when = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(MakePngBytes(20, 20));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "t.png");
        content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machine.Id,
            Timestamp = when,
            ClientTimeZoneId = "UTC",
            MonitorName = "manual"
        }, JsonTest.Options)), "metadata");
        var response = await _client!.PostAsync("/api/v1/screenshots", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var shot = await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        shot!.TimestampUtc.Should().Be(when);
        shot.ReceivedAtUtc.Should().BeAfter(when);

        var list = await _client.GetFromJsonAsync<List<ScreenshotResponse>>("/api/v1/screenshots?date=2026-01-15", JsonTest.Options);
        list.Should().Contain(s => s.Id == shot.Id);
    }

    [Fact]
    public async Task Upload_with_client_id_is_idempotent_on_retry()
    {
        var machine = await CreateMachineAsync("IdemBox");
        var clientId = Uuid7.NewUuid7();
        var when = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var bytes = MakePngBytes(16, 16);

        async Task<HttpResponseMessage> PostOnce()
        {
            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "idem.png");
            content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
            {
                Id = clientId,
                MachineId = machine.Id,
                Timestamp = when,
                ClientTimeZoneId = "UTC",
                MonitorName = "Monitor 1"
            }, JsonTest.Options)), "metadata");
            return await _client!.PostAsync("/api/v1/screenshots", content);
        }

        var first = await PostOnce();
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var shot1 = await first.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        shot1!.Id.Should().Be(clientId);

        var second = await PostOnce();
        second.IsSuccessStatusCode.Should().BeTrue(await second.Content.ReadAsStringAsync());
        var shot2 = await second.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options);
        shot2!.Id.Should().Be(clientId);

        var list = await _client!.GetFromJsonAsync<List<ScreenshotResponse>>(
            "/api/v1/screenshots?date=2026-03-01", JsonTest.Options);
        list!.Count(s => s.Id == clientId).Should().Be(1);
    }

    [Fact]
    public async Task Upload_with_same_client_id_but_different_content_gets_new_id()
    {
        var machine = await CreateMachineAsync("ClashBox");
        var clientId = Uuid7.NewUuid7();
        var when = new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

        async Task<ScreenshotResponse> PostBytes(byte[] bytes, string mon)
        {
            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "clash.png");
            content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
            {
                Id = clientId,
                MachineId = machine.Id,
                Timestamp = when,
                ClientTimeZoneId = "UTC",
                MonitorName = mon
            }, JsonTest.Options)), "metadata");
            var response = await _client!.PostAsync("/api/v1/screenshots", content);
            response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ScreenshotResponse>(JsonTest.Options))!;
        }

        var first = await PostBytes(MakePngBytes(12, 12), "A");
        first.Id.Should().Be(clientId);

        var second = await PostBytes(MakePngBytes(24, 24), "B");
        second.Id.Should().NotBe(clientId);
    }

    private async Task LoginAdminAsync()
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = _adminEmail,
            Password = AdminPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonTest.Options);
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.Token);
    }

    private async Task<MachineResponse> CreateMachineAsync(string name)
    {
        var create = await _client!.PostAsJsonAsync("/api/v1/machines", new CreateMachineRequest
        {
            Name = name + "-" + Guid.NewGuid().ToString("N")[..6],
            HardwareFingerprint = "fp-" + Guid.NewGuid().ToString("N")[..8]
        });
        create.EnsureSuccessStatusCode();
        return (await create.Content.ReadFromJsonAsync<MachineResponse>(JsonTest.Options))!;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        Uuid7 machineId, byte[] bytes, string contentType, string? monitor)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", "shot.png");
        content.Add(new StringContent(JsonSerializer.Serialize(new ScreenshotUploadMetadata
        {
            MachineId = machineId,
            Timestamp = DateTimeOffset.UtcNow,
            ClientTimeZoneId = "UTC",
            MonitorName = monitor
        }, JsonTest.Options)), "metadata");
        return await _client!.PostAsync("/api/v1/screenshots", content);
    }

    private static string ExifString(ExifProfile exif, ExifTag<string> tag)
    {
        exif.TryGetValue(tag, out var value).Should().BeTrue("missing EXIF tag " + tag);
        return value!.Value;
    }

    private static byte[] MakePngBytes(int w, int h)
    {
        using var image = new Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            image[x, y] = new Rgba32((byte)(x * 3), (byte)(y * 3), 100);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}
