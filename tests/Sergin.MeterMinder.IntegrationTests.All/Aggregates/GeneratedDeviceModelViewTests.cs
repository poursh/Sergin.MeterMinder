using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
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
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        CommandView remove = RemoveCommand(root);

        // The version the view loads the root at, then another model's add moves it.
        ViewSendResult loaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);
        Assert.False(loaded.IsError);
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"other-{marker}");

        ChildCommandOutcome outcome = await GeneratedChildCommands.SendAsync(dispatcher, remove, manufacturer.Value, model.Value, loaded.Version);

        // Refused as stale (the view reads again), and the model is still there.
        Assert.Equal(ChildCommandStatus.Refused, outcome.Status);
        Assert.True(outcome.Stale);
        Assert.True(await ModelExistsAsync(dispatcher, root, manufacturer, model));

        // After the re-read the same remove goes through.
        ViewSendResult reloaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);
        Assert.Equal(
            ChildCommandStatus.Sent, (await GeneratedChildCommands.SendAsync(dispatcher, remove, manufacturer.Value, model.Value, reloaded.Version)).Status);
        Assert.False(await ModelExistsAsync(dispatcher, root, manufacturer, model));
    }

    [Fact]
    public async Task DeviceModelDetail_RemoveWithNoRootVersion_SendsNothing()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();

        ChildCommandOutcome outcome = await GeneratedChildCommands.SendAsync(dispatcher, RemoveCommand(root), manufacturer.Value, model.Value, null);

        Assert.Equal(ChildCommandStatus.NoVersion, outcome.Status);
        Assert.True(await ModelExistsAsync(dispatcher, root, manufacturer, model));
    }

    [Fact]
    public async Task DeviceModelDetail_RemoveAtCurrentVersion_SendsAndLeavesForTheManufacturer()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        ViewSendResult loaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);

        ChildCommandOutcome outcome = await GeneratedChildCommands.SendAsync(dispatcher, RemoveCommand(root), manufacturer.Value, model.Value, loaded.Version);

        Assert.Equal(ChildCommandStatus.Sent, outcome.Status);
        Assert.Equal($"/dm/manufacturers/{manufacturer.Value}", GeneratedChildCommands.LeaveTarget(root, manufacturer.Value));
        Assert.False(await ModelExistsAsync(dispatcher, root, manufacturer, model));
    }

    private static CommandView RemoveCommand(AggregateViewModel root)
    {
        ChildViewModel child = Assert.Single(root.Children, candidate => candidate.Child == typeof(DeviceModel));
        CommandView remove = Assert.Single(child.Commands, command => command.Role == CommandRole.RemoveChild);
        Assert.Equal(typeof(RemoveDeviceModelCommand), remove.Request);

        return remove;
    }

    private static async Task<bool> ModelExistsAsync(ISerginDispatcher dispatcher, AggregateViewModel root, ManufacturerId manufacturer, DeviceModelInternalId model)
    {
        ChildViewModel child = Assert.Single(root.Children, candidate => candidate.Child == typeof(DeviceModel));
        ViewSendResult result = await child.GetOne!.Invoker.SendAsync(dispatcher, child.GetOne.Build(manufacturer.Value, model.Value));

        return !result.IsError;
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();

        return await client.GetStringAsync(new Uri(path, UriKind.Relative));
    }
}
