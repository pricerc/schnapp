namespace Schnapp.Client.Core.Tests;

using Medo;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Schnapp.Client.Core.Api;
using Schnapp.Client.Core.Upload;
using Schnapp.Contracts;

using Xunit;

public class UploadServiceTests
{
    [Fact]
    public void Enqueue_persists_and_lists()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-mut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var q = new UploadService(root, referenceDataDbPath: Path.Combine(root, "reference.db"));
            var id = q.Enqueue(MutationKind.JournalStop, new JournalStopPayload
            {
                JournalEntryId = Uuid7.NewUuid7()
            });
            id.Should().NotBe(Medo.Uuid7.Empty);
            q.ListItems().Should().ContainSingle(i => i.Id == id && i.Kind == MutationKind.JournalStop);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_orders_customers_before_projects()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-mut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var q = new UploadService(root, referenceDataDbPath: Path.Combine(root, "reference.db"));
            q.Enqueue(MutationKind.ListCreate, new ListCreatePayload
            {
                Kind = "projects",
                Item = new ListItemRequest { Id = Uuid7.NewUuid7(), Code = "p1", Name = "P1" }
            });
            q.Enqueue(MutationKind.ListCreate, new ListCreatePayload
            {
                Kind = "customers",
                Item = new ListItemRequest { Id = Uuid7.NewUuid7(), Code = "c1", Name = "C1" }
            });

            var order = new System.Collections.Generic.List<string>();
            var handler = new StubHandler(req =>
            {
                var path = req.RequestUri?.AbsolutePath ?? "";
                if (path.Contains("customers"))
                {
                    order.Add("customers");
                }

                if (path.Contains("projects"))
                {
                    order.Add("projects");
                }

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"code\":\"x\",\"name\":\"X\"}",
                        Encoding.UTF8, "application/json")
                };
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);
            var (ok, fail) = await q.PushAsync(api);
            ok.Should().Be(2);
            fail.Should().Be(0);
            order.Should().Equal("customers", "projects");
            q.PendingCount.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Push_marks_permanent_failure_dead_instead_of_retrying_forever()
    {
        var root = Path.Combine(Path.GetTempPath(), "schnapp-mut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var q = new UploadService(root, referenceDataDbPath: Path.Combine(root, "reference.db"));
            var id = q.Enqueue(MutationKind.JournalTargetEnsure, new JournalTargetEnsurePayload
            {
                Request = new JournalTargetRequest
                {
                    CustomerId = Uuid7.NewUuid7(),
                    ProjectId = Uuid7.NewUuid7(),
                    TaskId = Uuid7.NewUuid7()
                }
            });

            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"customer/project/task does not exist\"}", Encoding.UTF8, "application/json")
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
            using var api = new SchnappApiClient("http://localhost/", "tok", http);

            var (ok, fail) = await q.PushAsync(api);
            ok.Should().Be(0);
            fail.Should().Be(0); // dead items aren't counted as a retryable "failed" push
            q.PendingCount.Should().Be(0); // dead items are excluded from pending
            q.DeadCount.Should().Be(1);
            q.ListDeadItems().Should().ContainSingle(i => i.Id == id && i.Dead);

            // A second push should skip the dead item entirely rather than retrying it.
            var (ok2, fail2) = await q.PushAsync(api);
            ok2.Should().Be(0);
            fail2.Should().Be(0);

            q.ClearDeadItems().Should().Be(1);
            q.DeadCount.Should().Be(0);
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
