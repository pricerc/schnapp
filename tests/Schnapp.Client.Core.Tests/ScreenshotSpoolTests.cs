namespace Schnapp.Client.Core.Tests;

using Medo;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Config;
using Schnapp.Client.Core.Spool;
using Schnapp.Contracts;

using Xunit;

public class ScreenshotSpoolTests
{
    [Fact]
    public void Enqueue_and_status_survive_reload()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            var machineId = Uuid7.NewUuid7();
            var id = spool.Enqueue(
                new byte[] { 1, 2, 3, 4 },
                "t.png",
                "image/png",
                new ScreenshotUploadMetadata { MachineId = machineId });

            id.Should().NotBe(Medo.Uuid7.Empty);
            var status = spool.GetStatus();
            status.PendingCount.Should().Be(1);
            status.TotalBytes.Should().Be(4);

            var uploadMeta = spool.ReadUploadMetadata(id);
            uploadMeta.Should().NotBeNull();
            uploadMeta!.Id.Should().Be(id);
            uploadMeta.MachineId.Should().Be(machineId);

            var spool2 = new ScreenshotSpool(root);
            spool2.ListItems().Should().ContainSingle(i => i.Id == id);
            File.Exists(Path.Combine(root, Uuid7Text.Format(id), "meta.json")).Should().BeTrue();
            File.Exists(Path.Combine(root, Uuid7Text.Format(id), "upload.json")).Should().BeTrue();
            File.Exists(Path.Combine(root, Uuid7Text.Format(id), "image.bin")).Should().BeTrue();
            spool2.ReadUploadMetadata(id).Should().NotBeNull();
            spool2.ReadUploadMetadata(id)!.Id.Should().Be(id);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Reads_legacy_embedded_metadataJson_and_migrates()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var id = Uuid7.NewUuid7();
            var dir = Path.Combine(root, Uuid7Text.Format(id));
            Directory.CreateDirectory(dir);
            var machineId = Uuid7.NewUuid7();
            var uploadJson = $"{{\"machineId\":\"{machineId}\",\"monitorName\":\"desktop\"}}";
            var meta = $@"{{
  ""id"": ""{id}"",
  ""enqueuedUtc"": ""2026-01-01T00:00:00+00:00"",
  ""fileName"": ""t.jpg"",
  ""contentType"": ""image/jpeg"",
  ""metadataJson"": {System.Text.Json.JsonSerializer.Serialize(uploadJson)},
  ""attempts"": 0
}}";
            File.WriteAllText(Path.Combine(dir, "meta.json"), meta);
            File.WriteAllBytes(Path.Combine(dir, "image.bin"), new byte[] { 1 });

            var spool = new ScreenshotSpool(root);
            var read = spool.ReadUploadMetadata(id);
            read.Should().NotBeNull();
            read!.MachineId.Should().Be(machineId);
            read.MonitorName.Should().Be("desktop");

            // Listing triggers migrate to discrete upload.json
            spool.ListItems().Should().ContainSingle(i => i.Id == id);
            File.Exists(Path.Combine(dir, "upload.json")).Should().BeTrue();
            var metaText = File.ReadAllText(Path.Combine(dir, "meta.json"));
            metaText.Should().NotContain("metadataJson");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_removes_successful_items()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            spool.Enqueue(
                Encoding.UTF8.GetBytes("fake-png"),
                "t.png",
                "image/png",
                new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() });

            var handler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"userId\":\"00000000-0000-0000-0000-000000000002\",\"machineId\":\"00000000-0000-0000-0000-000000000003\",\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"receivedAtUtc\":\"2026-01-01T00:00:00Z\",\"contentType\":\"image/png\",\"byteSize\":1,\"createdAt\":\"2026-01-01T00:00:00Z\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);

            var (ok, fail) = await spool.PushAsync(api);
            ok.Should().Be(1);
            fail.Should().Be(0);
            spool.GetStatus().PendingCount.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SaveUploaded_creates_a_cached_item_not_a_pending_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            var serverId = Uuid7.NewUuid7();
            var id = spool.SaveUploaded(
                new byte[] { 1, 2, 3 },
                "t.png",
                "image/png",
                new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() },
                serverId);

            spool.ListItems().Should().BeEmpty(); // not pending
            var cached = spool.ListUploadedItems().Should().ContainSingle().Subject;
            cached.Id.Should().Be(id);
            cached.UploadStatus.Should().Be(SpoolUploadStatus.Uploaded);
            cached.ServerScreenshotId.Should().Be(serverId);
            cached.ByteSize.Should().Be(3);

            var status = spool.GetStatus();
            status.PendingCount.Should().Be(0);
            status.UploadedCount.Should().Be(1);
            status.UploadedBytes.Should().Be(3);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_with_retainUploaded_keeps_item_as_cached_instead_of_deleting()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            var pendingId = spool.Enqueue(
                Encoding.UTF8.GetBytes("fake-png"),
                "t.png",
                "image/png",
                new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() });

            var handler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"id\":\"00000000-0000-0000-0000-000000000009\",\"userId\":\"00000000-0000-0000-0000-000000000002\",\"machineId\":\"00000000-0000-0000-0000-000000000003\",\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"receivedAtUtc\":\"2026-01-01T00:00:00Z\",\"contentType\":\"image/png\",\"byteSize\":1,\"createdAt\":\"2026-01-01T00:00:00Z\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);

            var (ok, fail) = await spool.PushAsync(api, retainUploaded: true);
            ok.Should().Be(1);
            fail.Should().Be(0);

            spool.GetStatus().PendingCount.Should().Be(0);
            var cached = spool.ListUploadedItems().Should().ContainSingle().Subject;
            cached.Id.Should().Be(pendingId);
            cached.ServerScreenshotId.Should().Be(Uuid7Text.Parse("00000000-0000-0000-0000-000000000009"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_default_still_deletes_on_success_for_backward_compatibility()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            spool.Enqueue(
                Encoding.UTF8.GetBytes("fake-png"),
                "t.png",
                "image/png",
                new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() });

            var handler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"userId\":\"00000000-0000-0000-0000-000000000002\",\"machineId\":\"00000000-0000-0000-0000-000000000003\",\"timestampUtc\":\"2026-01-01T00:00:00Z\",\"receivedAtUtc\":\"2026-01-01T00:00:00Z\",\"contentType\":\"image/png\",\"byteSize\":1,\"createdAt\":\"2026-01-01T00:00:00Z\"}",
                        Encoding.UTF8,
                        "application/json")
                });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);

            await spool.PushAsync(api);

            spool.GetStatus().PendingCount.Should().Be(0);
            spool.ListUploadedItems().Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Housekeeping_evicts_oldest_uploaded_items_over_the_cache_size_cap()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            var meta = new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() };

            var oldest = spool.SaveUploaded(new byte[10], "a.png", "image/png", meta, Uuid7.NewUuid7());
            Thread.Sleep(10);
            var middle = spool.SaveUploaded(new byte[10], "b.png", "image/png", meta, Uuid7.NewUuid7());
            Thread.Sleep(10);
            var newest = spool.SaveUploaded(new byte[10], "c.png", "image/png", meta, Uuid7.NewUuid7());

            // Cap is smaller than the 30 total bytes cached, so the oldest item(s) must go first.
            var housekeeping = new ScreenshotCacheHousekeeping(spool, new CacheSettings { MaxCacheBytes = 20 });
            var evicted = housekeeping.Enforce();

            evicted.Should().BeGreaterThan(0);
            var remainingIds = spool.ListUploadedItems().Select(i => i.Id).ToList();
            remainingIds.Should().NotContain(oldest);
            remainingIds.Should().Contain(newest);
            (spool.GetStatus().UploadedBytes).Should().BeLessThanOrEqualTo(20);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_keeps_item_on_failure_with_backoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-spool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var spool = new ScreenshotSpool(root);
            spool.Enqueue("\t"u8.ToArray(), "t.png", "image/png",
                new ScreenshotUploadMetadata { MachineId = Uuid7.NewUuid7() });

            var handler = new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("down")
                });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);

            var (ok, fail) = await spool.PushAsync(api);
            ok.Should().Be(0);
            fail.Should().Be(1);
            var item = spool.ListItems().Should().ContainSingle().Subject;
            item.Attempts.Should().Be(1);
            item.LastError.Should().NotBeNullOrEmpty();
            item.NextAttemptUtc.Should().NotBeNull();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }
}
