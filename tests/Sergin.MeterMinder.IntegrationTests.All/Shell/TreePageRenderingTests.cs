using System.Net;
using System.Text.RegularExpressions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.UserAccess.Domain.Scopes;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The scope tree layout as the prerender draws it: the page header keeps the tree's own trail, the panel holds the
/// tree, and the page at the route — <c>/ua/scopes</c>, a scope, a new scope — sits in the pane and names its node,
/// which shows selected with its ancestors expanded, without a second page title or header strip.
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
        Assert.Contains("No node selected", html, StringComparison.Ordinal);

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

        // The view sits in the pane: one header strip (the tree's, its own step now a link back), one title.
        Assert.Single(BreadcrumbNav().Matches(html));
        Assert.Contains("href=\"/ua/scopes\"", BreadcrumbNav().Match(html).Value, StringComparison.Ordinal);
        Assert.Equal("Scopes", PageTitle().Match(html).Groups[1].Value.Trim());
        Assert.Contains("No child scopes.", html, StringComparison.Ordinal);
        Assert.Contains("North Region", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No node selected", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_SelectsTheParent_AndMountsTheFormInThePane()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/new?parentId={scenario.North}");

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.Matches($"aria-selected=\"true\"[^>]*data-node=\"{scenario.North}\"", html);
        Assert.Contains("New scope", TreeHeader(html), StringComparison.Ordinal);
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

    // From the tree header to the content body below it.
    private static string TreeHeader(string html)
    {
        int start = html.IndexOf("sergin-tree-page-header d-flex", StringComparison.Ordinal);
        int end = html.IndexOf("sergin-tree-body", start, StringComparison.Ordinal);

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
