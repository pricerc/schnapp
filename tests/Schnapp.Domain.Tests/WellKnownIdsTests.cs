using FluentAssertions;
using Medo;
using Schnapp.Contracts;
using Schnapp.Domain;

namespace Schnapp.Domain.Tests;

/// <summary>Unit tests for well-known sentinel IDs and ID generation.</summary>
public class WellKnownIdsTests
{
    [Fact]
    public void None_is_empty_uuid7()
    {
        WellKnownIds.None.Should().Be(Uuid7.Empty);
    }

    [Fact]
    public void NewId_is_not_none()
    {
        IdFactory.NewId().Should().NotBe(WellKnownIds.None);
    }

    [Fact]
    public void NewId_is_uuid_version_7()
    {
        var id = IdFactory.NewId();
        id.Version.Should().Be(7);
    }

    [Fact]
    public void NewId_is_text_sortable()
    {
        var a = Uuid7Text.Format(IdFactory.NewId());
        var b = Uuid7Text.Format(IdFactory.NewId());
        string.CompareOrdinal(a, b).Should().BeLessThanOrEqualTo(0);
    }

    [Fact]
    public void NewId_is_binary_sortable_rfc_order()
    {
        var a = IdFactory.NewId().ToByteArray();
        var b = IdFactory.NewId().ToByteArray();
        // lexicographic compare of RFC bytes should be non-decreasing
        var cmp = 0;
        for (var i = 0; i < 16 && cmp == 0; i++)
            cmp = a[i].CompareTo(b[i]);
        cmp.Should().BeLessThanOrEqualTo(0);
    }
}
