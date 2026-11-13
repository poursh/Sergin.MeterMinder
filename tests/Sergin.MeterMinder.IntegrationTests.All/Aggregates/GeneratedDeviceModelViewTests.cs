using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
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

    [Fact]
    public async Task AddDeviceModel_RendersNameInputTitleAndTrail()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}/models/new");

        Assert.Contains("New device model", html, StringComparison.Ordinal);
        Assert.Contains("Name", html, StringComparison.Ordinal);
        Assert.Contains("<input", html, StringComparison.Ordinal);
        Assert.Contains($"maker-{marker}", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/dm/manufacturers\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Create<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddDeviceModel_UnknownManufacturer_ShowsProblemPanelWithLinkedTrail()
    {
        var unknown = Guid.CreateVersion7();

        string html = await GetAsync($"/dm/manufacturers/{unknown}/models/new");

        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{unknown}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameDeviceModel_PrefillsNameAndTrail()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}/edit");

        Assert.Contains($"value=\"model-{marker}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Rename<", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Save<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameDeviceModel_StaleSave_ReloadsForm()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        ChildViewModel child = Assert.Single(root.Children, candidate => candidate.Child == typeof(DeviceModel));
        Assert.True(GeneratedChildForm.RequiresVersion(child, isEdit: true));
        Dictionary<string, object?> values = new(StringComparer.Ordinal) { [nameof(RenameDeviceModelCommand.Name)] = $"renamed-{marker}" };

        // The version the view loads the root at, then another model's add moves it.
        ViewSendResult loaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);
        Assert.False(loaded.IsError);
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"other-{marker}");

        ChildSubmitOutcome stale = await GeneratedChildForm.SubmitAsync(dispatcher, child, manufacturer.Value, model.Value, values, loaded.Version);

        // One stale refusal, nothing renamed; the re-read version then goes through and the form leaves for the model.
        Assert.Equal(ChildSubmitStatus.Refused, stale.Status);
        Assert.True(stale.Stale);
        Assert.Single(stale.Errors);
        Assert.Equal($"model-{marker}", await ModelNameAsync(dispatcher, child, manufacturer, model));

        ViewSendResult reloaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);
        ChildSubmitOutcome sent = await GeneratedChildForm.SubmitAsync(dispatcher, child, manufacturer.Value, model.Value, values, reloaded.Version);

        Assert.Equal(ChildSubmitStatus.Sent, sent.Status);
        Assert.Equal($"renamed-{marker}", await ModelNameAsync(dispatcher, child, manufacturer, model));
        Assert.Equal(
            $"/dm/manufacturers/{manufacturer.Value}/models/{model.Value}",
            GeneratedChildForm.LeaveTarget(root, child, manufacturer.Value, model.Value, null));
    }

    [Fact]
    public async Task RenameDeviceModel_WithNoRootVersion_SendsNothing()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        ChildViewModel child = Assert.Single(root.Children, candidate => candidate.Child == typeof(DeviceModel));
        Dictionary<string, object?> values = new(StringComparer.Ordinal) { [nameof(RenameDeviceModelCommand.Name)] = $"renamed-{marker}" };

        ChildSubmitOutcome outcome = await GeneratedChildForm.SubmitAsync(dispatcher, child, manufacturer.Value, model.Value, values, null);

        Assert.Equal(ChildSubmitStatus.NoVersion, outcome.Status);
        Assert.Equal($"model-{marker}", await ModelNameAsync(dispatcher, child, manufacturer, model));
    }

    [Fact]
    public async Task AddDeviceModel_Submit_AddsAndLeavesForTheNewModel()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        AggregateViewModel root = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        ChildViewModel child = Assert.Single(root.Children, candidate => candidate.Child == typeof(DeviceModel));
        Dictionary<string, object?> values = new(StringComparer.Ordinal) { [nameof(AddDeviceModelCommand.Name)] = $"model-{marker}" };
        ViewSendResult loaded = await root.LoadRecordAsync(dispatcher, manufacturer.Value);

        ChildSubmitOutcome outcome = await GeneratedChildForm.SubmitAsync(dispatcher, child, manufacturer.Value, null, values, loaded.Version);

        Assert.Equal(ChildSubmitStatus.Sent, outcome.Status);
        Guid created = Assert.NotNull(outcome.CreatedId);
        Assert.Equal(
            $"/dm/manufacturers/{manufacturer.Value}/models/{created}",
            GeneratedChildForm.LeaveTarget(root, child, manufacturer.Value, null, outcome.CreatedId));
        Assert.Equal($"/dm/manufacturers/{manufacturer.Value}", GeneratedChildForm.RootHref(root, manufacturer.Value));
    }

    private static async Task<string?> ModelNameAsync(ISerginDispatcher dispatcher, ChildViewModel child, ManufacturerId manufacturer, DeviceModelInternalId model)
    {
        ViewSendResult result = await child.GetOne!.Invoker.SendAsync(dispatcher, child.GetOne.Build(manufacturer.Value, model.Value));

        return (result.Value as DeviceModelQueryResponse)?.Name;
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
