using FluentAssertions;
using Schnapp.Web.Pages.Reports;
using Xunit;

namespace Schnapp.Web.Tests;

/// <summary>Unit tests for report sort/group helpers (no host / DB).</summary>
public class ReportGroupingTests
{
    [Fact]
    public void BuildSections_flat_sorts_by_duration_desc()
    {
        var rows = new[]
        {
            Row("2026-07-01", "Acme", "P1", "T1", 100),
            Row("2026-07-02", "Beta", "P2", "T2", 500),
            Row("2026-07-01", "Acme", "P1", "T2", 200)
        };

        var sections = IndexModel.BuildSections(rows, "none", "duration", "desc");
        sections.Should().ContainSingle();
        sections[0].ShowSubtotal.Should().BeFalse();
        sections[0].Rows.Select(r => r.TotalSeconds).Should().Equal(500L, 200L, 100L);
    }

    [Fact]
    public void BuildSections_group_by_customer_emits_subtotals()
    {
        var rows = new[]
        {
            Row("2026-07-01", "Acme", "P1", "T1", 100),
            Row("2026-07-02", "Beta", "P2", "T2", 50),
            Row("2026-07-03", "Acme", "P1", "T2", 25)
        };

        var sections = IndexModel.BuildSections(rows, "customer", "date", "asc");
        sections.Should().HaveCount(2);
        sections.Should().OnlyContain(s => s.ShowHeader && s.ShowSubtotal);

        var acme = sections.Single(s => s.HeaderLabel == "Acme");
        acme.Rows.Should().HaveCount(2);
        acme.SubtotalSeconds.Should().Be(125);
        acme.SubtotalEntryCount.Should().Be(2);

        var beta = sections.Single(s => s.HeaderLabel == "Beta");
        beta.SubtotalSeconds.Should().Be(50);
    }

    [Fact]
    public void BuildSections_group_customer_project_task_uses_composite_key()
    {
        var rows = new[]
        {
            Row("2026-07-01", "Acme", "P1", "T1", 10),
            Row("2026-07-02", "Acme", "P1", "T2", 20),
            Row("2026-07-03", "Acme", "P1", "T1", 5)
        };

        var sections = IndexModel.BuildSections(rows, "customer-project-task", "date", "asc");
        sections.Should().HaveCount(2);
        sections.Select(s => s.HeaderLabel).Should().BeEquivalentTo(new[]
        {
            "Acme › P1 › T1",
            "Acme › P1 › T2"
        });
        sections.Single(s => s.HeaderLabel!.EndsWith("T1")).SubtotalSeconds.Should().Be(15);
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("CUSTOMER", "customer")]
    [InlineData("customer-project", "customer-project")]
    [InlineData("bogus", "none")]
    public void NormalizeGroupBy_maps_aliases(string? input, string expected) =>
        IndexModel.NormalizeGroupBy(input).Should().Be(expected);

    private static IndexModel.ReportRowView Row(
        string date, string customer, string project, string task, long seconds) =>
        new()
        {
            LocalDate = date,
            CustomerLabel = customer,
            ProjectLabel = project,
            TaskLabel = task,
            CustomerCode = customer,
            ProjectCode = project,
            TaskCode = task,
            TotalSeconds = seconds,
            EntryCount = 1,
            DurationDisplay = IndexModel.FormatDuration(seconds)
        };
}
