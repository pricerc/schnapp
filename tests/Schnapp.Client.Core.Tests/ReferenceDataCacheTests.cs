namespace Schnapp.Client.Core.Tests;

using Medo;

using System;
using System.IO;

using FluentAssertions;

using Schnapp.Client.Core.Upload;
using Schnapp.Contracts;

using Xunit;

public class ReferenceDataCacheTests
{
    [Fact]
    public void Replace_then_get_round_trips_and_replaces_wholesale()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "schnapp-refcache-" + Guid.NewGuid().ToString("N"), "reference.db");
        try
        {
            using var cache = new ReferenceDataCache(dbPath);

            var customerId = Uuid7.NewUuid7();
            cache.ReplaceCustomers(new[]
            {
                new ListItemResponse { Id = customerId, Code = "acme", Name = "Acme" }
            });

            var projectId = Uuid7.NewUuid7();
            cache.ReplaceProjects(new[]
            {
                new ListItemResponse { Id = projectId, Code = "web", Name = "WebApp", CustomerId = customerId }
            });

            cache.GetCustomers().Should().ContainSingle(c => c.Id == customerId && c.Name == "Acme");
            cache.GetProjects().Should().ContainSingle(p => p.Id == projectId && p.CustomerId == customerId);

            // Replace wholesale — old rows should be gone.
            cache.ReplaceCustomers(new[]
            {
                new ListItemResponse { Id = Uuid7.NewUuid7(), Code = "other", Name = "Other" }
            });
            cache.GetCustomers().Should().NotContain(c => c.Id == customerId);
        }
        finally
        {
            var dir = Path.GetDirectoryName(dbPath)!;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Survives_reopen_of_same_db_file()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "schnapp-refcache-" + Guid.NewGuid().ToString("N"), "reference.db");
        try
        {
            var targetId = Uuid7.NewUuid7();
            using (var cache = new ReferenceDataCache(dbPath))
            {
                cache.ReplaceJournalTargets(new[]
                {
                    new JournalTargetResponse
                    {
                        Id = targetId,
                        CustomerId = Uuid7.NewUuid7(),
                        ProjectId = Uuid7.NewUuid7(),
                        TaskId = Uuid7.NewUuid7(),
                        Name = "Reopen test"
                    }
                });
            }

            using var reopened = new ReferenceDataCache(dbPath);
            reopened.GetJournalTargets().Should().ContainSingle(t => t.Id == targetId);
        }
        finally
        {
            var dir = Path.GetDirectoryName(dbPath)!;
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
