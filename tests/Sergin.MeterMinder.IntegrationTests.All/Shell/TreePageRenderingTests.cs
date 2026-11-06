using System.Net;
using System.Text.RegularExpressions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.Administration.UserAccess.Domain.Scopes;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The scope tree layout as the prerender draws it: the page header carries the tree's own trail and the selected
/// node's path, the panel holds the tree, and the page at the route — <c>/ua/scopes</c>, a scope, a new scope — sits
/// in the pane and names its node, which shows selected with its ancestors expanded, without a second page title or
/// header strip.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class TreePageRenderingTests(SerginWebApiFactory<Program> factory)
{
    [GeneratedRegex("<nav aria-label=\"Breadcrumb\">.*?</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbNav();

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex PageTitle();

    [Fact]
    public async Task Unselected_ShowsTheTreeAndThePrompt()
    {
        string html = await GetAsync("/ua/scopes");

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-node=\"{Scope.RootId.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains("Select a scope", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sergin-tree-page-header", html, StringComparison.Ordinal);

        // Unselected: the page's own step is the current page, not a link.
        Assert.DoesNotContain("href=\"/ua/scopes\"", BreadcrumbNav().Match(html).Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_ExpandsTheAncestors_AndMountsTheViewInThePane()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.Feeder1}");

        // The path down to the node is open, and the node is the selected row.
        Assert.Contains($"data-node=\"{scenario.North}\"", html, StringComparison.Ordinal);
        Assert.Matches($"aria-selected=\"true\"[^>]*data-node=\"{scenario.Feeder1}\"", html);

        // The view sits in the pane: one header strip, one title. The strip goes on from the tree's step, now a link
        // back, through the ancestors, each a link, to the node; the view's header buttons sit in it.
        Assert.Single(BreadcrumbNav().Matches(html));
        string trail = BreadcrumbNav().Match(html).Value;
        Assert.Contains("href=\"/ua/scopes\"", trail, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/scopes/{scenario.North}\"", trail, StringComparison.Ordinal);
        Assert.Matches("aria-current=\"page\"[^>]*>Feeder 1<", trail);
        Assert.Contains("aria-label=\"Copy link\"", PageHeader(html), StringComparison.Ordinal);
        Assert.Equal("Scopes", PageTitle().Match(html).Groups[1].Value.Trim());
        Assert.Contains("No child scopes.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("sergin-tree-page-header", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_SelectsTheParent_AndMountsTheFormInThePane()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/new?parentId={scenario.North}");

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.Matches($"aria-selected=\"true\"[^>]*data-node=\"{scenario.North}\"", html);
        Assert.Contains($"href=\"/ua/scopes/{scenario.North}\"", BreadcrumbNav().Match(html).Value, StringComparison.Ordinal);
        Assert.Matches("aria-current=\"page\"[^>]*>New scope<", BreadcrumbNav().Match(html).Value);
        Assert.Matches("<button[^>]*type=\"submit\"[^>]*>(?:(?!</button>).)*Create", html);
        Assert.Single(BreadcrumbNav().Matches(html));
        Assert.Equal("Scopes", PageTitle().Match(html).Groups[1].Value.Trim());
    }

    [Fact]
    public async Task Create_WithoutAParent_SelectsTheRoot()
    {
        string html = await GetAsync("/ua/scopes/new");

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.Matches($"aria-selected=\"true\"[^>]*data-node=\"{Scope.RootId.Value}\"", html);
    }

    [Fact]
    public async Task UnknownNode_ShowsNotFound_AndSelectsNothing()
    {
        string html = await GetAsync($"/ua/scopes/{Guid.CreateVersion7()}");

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch("aria-selected=\"true\"", html);
        Assert.Contains("mud-alert", html, StringComparison.Ordinal);
    }

    // From the page header to the tree below it.
    private static string PageHeader(string html)
    {
        int start = html.IndexOf("sergin-page-header mb-4", StringComparison.Ordinal);
        int end = html.IndexOf("sergin-tree-page", start, StringComparison.Ordinal);

        return html[start..end];
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
