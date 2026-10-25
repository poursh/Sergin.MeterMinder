using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The aggregate page types as the prerender draws them: the page header's icon buttons beside the trail, the
/// overview slot and the list's search and filter controls on a list page, and on a detail page the actions bar,
/// the labelled fields and the child section. MudTable's rows load after first render, so the rows themselves are
/// the read-model tests' job, not these.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class PageTypeRenderingTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task DeviceListPage_RendersHeaderIcons_Overview_SearchAndFilter()
    {
        string html = await GetAsync("/dm/devices");

        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Overview\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Quick search\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Advanced filter\"", html, StringComparison.Ordinal);
        Assert.Contains("New device", html, StringComparison.Ordinal);
        Assert.DoesNotContain("not implemented yet", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceListPage_RendersTheSelectionColumn_ButNoSelectionBar()
    {
        string html = await GetAsync("/dm/devices");

        // The bulk Delete gives the table its header checkbox; with nothing selected there is no bar.
        Assert.Contains("mud-checkbox", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Selection\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceDetailPage_RendersAuditIcon_Actions_AndFields()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);

        string html = await GetAsync($"/dm/devices/{device}");

        Assert.Contains("aria-label=\"Copy link\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Audit &amp; version\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/devices/{device}/edit\"", html, StringComparison.Ordinal);
        Assert.Contains(">Delete", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains($">maker-{marker}</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManufacturerDetailPage_RendersTheModelsChildSection_AndTheCollapsedOverview()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{CatalogSeed.Marker()}", "14 Foundry Lane");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}");

        Assert.Contains("aria-label=\"Device models\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/models/new\"", html, StringComparison.Ordinal);
        Assert.Contains("14 Foundry Lane", html, StringComparison.Ordinal);
        Assert.Contains("Expand", html, StringComparison.Ordinal);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
