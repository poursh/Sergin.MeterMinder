using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The page/view split as the prerender draws it: a route page hosts one view in <c>SerginPage</c>, which gives
/// that view the page title and the header row; a view nested inside it is embedded — a titled section that claims
/// neither. Form views keep the MudForm shape the create and edit pages had.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class ViewHostRenderingTests(SerginWebApiFactory<Program> factory)
{
    [GeneratedRegex("<nav aria-label=\"Breadcrumb\">.*?</nav>", RegexOptions.Singleline)]
    private static partial Regex BreadcrumbNav();

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.Singleline)]
    private static partial Regex PageTitle();

    [Fact]
    public async Task NestedView_RendersAsASection_AndClaimsNoHeader()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string name = $"maker-{CatalogSeed.Marker()}";
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, name);

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}");

        // One header row: the manufacturer view's. The embedded model list adds no second strip and no step.
        Assert.Single(BreadcrumbNav().Matches(html));
        Assert.DoesNotContain("Device models", BreadcrumbNav().Match(html).Value, StringComparison.Ordinal);
        Assert.Equal("Manufacturer", PageTitle().Match(html).Groups[1].Value.Trim());

        // The model list is an embedded view: an outlined section titled with the collection.
        Assert.Matches("<section class=\"sergin-view sergin-entity-list-section[^\"]*\" aria-label=\"Device models\"", html);
    }

    [Fact]
    public async Task EntityDetailView_RendersFields_AndPermissionGatedActions()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}");

        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}/edit\"", html, StringComparison.Ordinal);
        Assert.Contains(">Remove", html, StringComparison.Ordinal);
        Assert.Contains($">maker-{marker}</a>", html, StringComparison.Ordinal);
        Assert.Contains("sergin-detail", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dm/devices/new", "New device", "Create", "/dm/devices")]
    [InlineData("/dm/manufacturers/new", "New manufacturer", "Create", "/dm/manufacturers")]
    public async Task FormView_OnAPage_RendersTitleTrailAndForm(string path, string title, string submit, string cancelHref)
    {
        string html = await GetAsync(path);

        Assert.Equal(title, PageTitle().Match(html).Groups[1].Value.Trim());
        Assert.Matches(BreadcrumbNav(), html);
        Assert.Contains("<form", html, StringComparison.Ordinal);
        Assert.Matches($"type=\"submit\"[^>]*>[^<]*(<[^>]*>)*{submit}", html);
        Assert.Contains($"href=\"{cancelHref}\"", html, StringComparison.Ordinal);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
