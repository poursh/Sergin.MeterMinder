using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>What the generated device model detail page shows: its fields, trail and actions, its not-found path, and the stale remove.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedDeviceModelViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task DeviceModelDetail_RendersFieldsTrailAndActions()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}");

        Assert.Contains($"model-{marker}", html, StringComparison.Ordinal);
        Assert.Contains($"maker-{marker}", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains(">ID<", html, StringComparison.Ordinal);
        Assert.Contains(model.Value.ToString(), html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}/edit\"", html, StringComparison.Ordinal);
        Assert.Contains(">Remove", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/dm/manufacturers\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceModelDetail_UnknownModel_ShowsProblemPanelWithLinkedTrail()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{CatalogSeed.Marker()}");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}/models/{Guid.CreateVersion7()}");

        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceModelDetail_StaleRemove_ReloadsAndKeepsModel()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        ChildViewModel child = Assert.Single(
            factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>().Children,
            candidate => candidate.Child == typeof(DeviceModel));
        CommandView remove = Assert.Single(child.Commands, command => command.Role == CommandRole.RemoveChild);
        Assert.Equal(typeof(RemoveDeviceModelCommand), remove.Request);

        // The detail view's remove is this send: at the version it loaded, which another model's add has since moved past.
        ViewSendResult stale = await GeneratedChildBulk.SendAsync(dispatcher, remove, manufacturer.Value, model.Value, RowVersion.Create());

        Assert.True(stale.IsError);
        Assert.Contains(stale.Errors, VersionErrors.IsStale);

        // The view reads the model again and still finds it.
        ViewSendResult reloaded = await child.GetOne!.Invoker.SendAsync(dispatcher, child.GetOne.Build(manufacturer.Value, model.Value));
        Assert.False(reloaded.IsError);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();

        return await client.GetStringAsync(new Uri(path, UriKind.Relative));
    }
}
