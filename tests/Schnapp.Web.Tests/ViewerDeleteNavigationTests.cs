using FluentAssertions;
using Schnapp.Web.Pages;
using Xunit;

namespace Schnapp.Web.Tests;

/// <summary>Which image the home viewer opens after deleting the current one.</summary>
public class ViewerDeleteNavigationTests
{
    [Fact]
    public void Next_image_follows_current_sort_order()
    {
        var order = new[] { "a", "b", "c" };

        IndexModel.NextViewerShotAfterDelete(order, "a").Should().Be("b");
        IndexModel.NextViewerShotAfterDelete(order, "b").Should().Be("c");
    }

    [Fact]
    public void Last_image_falls_back_to_previous()
    {
        IndexModel.NextViewerShotAfterDelete(new[] { "a", "b", "c" }, "c").Should().Be("b");
    }

    [Fact]
    public void Only_image_closes_the_viewer()
    {
        IndexModel.NextViewerShotAfterDelete(new[] { "only" }, "only").Should().BeNull();
        IndexModel.NextViewerShotAfterDelete(Array.Empty<string>(), "missing").Should().BeNull();
    }

    [Fact]
    public void Image_not_on_the_page_closes_the_viewer()
    {
        IndexModel.NextViewerShotAfterDelete(new[] { "a", "b" }, "z").Should().BeNull();
    }
}
