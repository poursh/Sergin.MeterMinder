using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated Manufacturer views show. A list's rows load after the first render, so the prerender pins the
/// chrome (columns, search, create, the model section's title and Add) and the view model and the pure row decision
/// pin what each row carries (links, edit and remove commands, the in-use rule); the bulk run is called directly.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedManufacturerViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task ManufacturerList_RendersColumnsSearchAndCreate()
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/dm/manufacturers", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Search name or address", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/dm/manufacturers/new\"", html, StringComparison.Ordinal);
        Assert.Contains(">New manufacturer", html, StringComparison.Ordinal);
        Assert.Contains(">Name<", html, StringComparison.Ordinal);
        Assert.Contains(">Address<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ManufacturerList_RowCommands_EditLinkAndDelete()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>();
        var id = Guid.CreateVersion7();

        // Each row's Name links to the detail page and its Edit icon to the edit page.
        Assert.Equal($"/dm/manufacturers/{id}", model.Routes.Detail!(id));
        Assert.Equal($"/dm/manufacturers/{id}/edit", model.Routes.Edit!(id));
        Assert.Equal(nameof(GetManufacturerListItem.Name), model.ListTitleField, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(model.Commands, command => command.Request == typeof(UpdateManufacturerCommand));

        // Delete is the row icon and the bulk action, so it runs once per selected row.
        CommandView delete = Assert.Single(model.Commands, command => command.Request == typeof(DeleteManufacturerCommand));
        Assert.True(delete.Selectable);
        Assert.NotNull(delete.ConfirmManyText);
    }

    [Fact]
    public async Task ManufacturerDetail_RendersFieldsOverviewAndModelSection()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}", "14 Foundry Lane");

        string html = await GetAsync($"/dm/manufacturers/{manufacturer.Value}");

        Assert.Contains($"maker-{marker}", html, StringComparison.Ordinal);
        Assert.Contains("14 Foundry Lane", html, StringComparison.Ordinal);
        Assert.Contains("Device models", html, StringComparison.Ordinal);
        Assert.Contains("Add model", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/models/new\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Information\"", html, StringComparison.Ordinal);
        Assert.Contains(ManufacturerUiTexts.SummaryModels.Default, html, StringComparison.Ordinal);
        Assert.Contains(ManufacturerUiTexts.SummaryDevices.Default, html, StringComparison.Ordinal);

        // The rows load after the first render, so what each row carries is read from the model: the name links to the
        // model's page, and the device count renders through its text.
        ChildViewModel child = ModelChild();
        var modelId = Guid.CreateVersion7();
        Assert.Equal($"/dm/manufacturers/{manufacturer.Value}/models/{modelId}", child.Routes.Detail!(manufacturer.Value, modelId));
        Assert.Equal(nameof(GetDeviceModelListItem.Name), child.ListTitleField, StringComparer.OrdinalIgnoreCase);
        FieldView count = Assert.Single(child.ListFields, field => field.Name == nameof(GetDeviceModelListItem.DeviceCount));
        Assert.Equal(DeviceModelUiTexts.DeviceCount, count.Format);
        Assert.Equal(
            "3 devices",
            FieldValueText.Render(
                count, 3, scope.ServiceProvider.GetRequiredService<ICultureFormatter>(), scope.ServiceProvider.GetRequiredService<IStringLocalizer>()));
        Assert.Equal(typeof(GeneratedChildListBody<GetDeviceModelListItem>), child.ListBodyType);
    }

    [Fact]
    public void ManufacturerDetail_InUseModel_RemoveDisabledWithTooltip()
    {
        ChildViewModel child = ModelChild();
        var manufacturer = Guid.CreateVersion7();
        var version = RowVersion.Create();
        GetDeviceModelListItem free = new(Guid.CreateVersion7(), "free", 0);
        GetDeviceModelListItem inUse = new(Guid.CreateVersion7(), "busy", 2);

        IReadOnlyList<ChildRowAction> freeRow = GeneratedChildListBody<GetDeviceModelListItem>.RowActionsFor(child, manufacturer, version, free);
        IReadOnlyList<ChildRowAction> busyRow = GeneratedChildListBody<GetDeviceModelListItem>.RowActionsFor(child, manufacturer, version, inUse);
        IReadOnlyList<ChildRowAction> noVersion = GeneratedChildListBody<GetDeviceModelListItem>.RowActionsFor(child, manufacturer, null, free);

        // Rename links to the model's edit page; Remove runs, and is the one a model in use disables.
        ChildRowAction rename = Assert.Single(freeRow, row => row.Href is not null);
        Assert.Equal($"/dm/manufacturers/{manufacturer}/models/{free.Id}/edit", rename.Href);
        Assert.False(rename.Disabled);
        ChildRowAction remove = Assert.Single(freeRow, row => row.Command.Request == typeof(RemoveDeviceModelCommand));
        Assert.False(remove.Disabled);
        Assert.Null(remove.DisabledReason);

        ChildRowAction busyRemove = Assert.Single(busyRow, row => row.Command.Request == typeof(RemoveDeviceModelCommand));
        Assert.True(busyRemove.Disabled);
        Assert.Equal(DeviceModelUiTexts.InUseTooltip, busyRemove.DisabledReason);
        Assert.False(Assert.Single(busyRow, row => row.Href is not null).Disabled);

        // Without the manufacturer's version no icon is usable, and none claims the in-use reason.
        Assert.All(noVersion, row => Assert.True(row.Disabled));
        Assert.All(noVersion, row => Assert.Null(row.DisabledReason));
    }

    [Fact]
    public async Task ManufacturerDetail_ReadOnlyUser_ShowsModelsWithoutCommands()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}", "14 Foundry Lane");
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller reader = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, ManufacturerPermissions.Read.Permission);

        string admin = await GetAsync($"/dm/manufacturers/{manufacturer.Value}");
        string html = await GetAsAsync(reader, $"/dm/manufacturers/{manufacturer.Value}");

        // The control: the dev administrator sees the commands the reader must not.
        Assert.Contains("Add model", admin, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/manufacturers/{manufacturer.Value}/edit\"", admin, StringComparison.Ordinal);
        Assert.Contains(">Delete", admin, StringComparison.Ordinal);
        Assert.Contains("mud-checkbox", admin, StringComparison.Ordinal);

        // No bulk action the reader may run, so the table has no selection column (its header checkbox is in the prerender).
        Assert.DoesNotContain("mud-checkbox", html, StringComparison.Ordinal);

        Assert.Contains($"maker-{marker}", html, StringComparison.Ordinal);
        Assert.Contains("Device models", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Add model", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/models/new", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"/dm/manufacturers/{manufacturer.Value}/edit\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Delete", html, StringComparison.Ordinal);

        // Row icons are hidden by the same permission check, whose permission the row's commands carry.
        Assert.All(
            ModelChild().Commands.Where(command => command.Role is CommandRole.RemoveChild or CommandRole.AddChild),
            command => Assert.Contains(ManufacturerPermissions.Update, command.Permissions));
    }

    [Fact]
    public async Task ManufacturerDetail_BulkRemove_ChainsVersionsAndRefusesInUseAlone()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId first = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"first-{marker}");
        DeviceModelInternalId middle = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"middle-{marker}");
        DeviceModelInternalId last = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"last-{marker}");
        await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", middle);

        ChildViewModel child = ModelChild();
        CommandView remove = Assert.Single(child.Commands, command => command.Role == CommandRole.RemoveChild);
        Assert.True(remove.Selectable);
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturer));
        Assert.False(loaded.IsError);

        (SerginBulkOutcome outcome, RowVersion? version) = await GeneratedChildBulk.RemoveAsync(
            dispatcher, remove, manufacturer.Value, loaded.Value.Version, [first.Value, middle.Value, last.Value]);

        Assert.Equal(2, outcome.Succeeded);
        Assert.Equal(0, outcome.Skipped);
        Error refusal = Assert.Single(outcome.Errors);
        Assert.Equal(ErrorType.Validation, refusal.Type);

        // The chain ended on the manufacturer's stored version, so a write at it is not stale.
        ErrorOr<Versioned<ManufacturerQueryResponse>> reloaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturer));
        Assert.Equal(reloaded.Value.Version, version);

        ViewSendResult models = await child.List!.Invoker.SendAsync(
            dispatcher, child.List.Build(manufacturer.Value, new ListQueryInput(Paggination.Create(50, 1), null, [], [])));
        var rows = (ListQueryResponse<GetDeviceModelListItem>)models.Value!;
        GetDeviceModelListItem remaining = Assert.Single(rows.Data);
        Assert.Equal(middle.Value, remaining.Id);
    }

    [Fact]
    public async Task ManufacturerDetail_StaleDelete_ReloadsAndKeepsManufacturer()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        CommandView remove = Assert.Single(ModelChild().Commands, command => command.Role == CommandRole.RemoveChild);
        Assert.True(remove.RequiresVersion);

        // A model removed at a version the manufacturer has moved past: refused as stale, and nothing is lost.
        ViewSendResult stale = await GeneratedChildBulk.SendAsync(dispatcher, remove, manufacturer.Value, model.Value, RowVersion.Create());

        Assert.True(stale.IsError);
        Assert.Contains(stale.Errors, VersionErrors.IsStale);
        Assert.False((await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturer))).IsError);

        (SerginBulkOutcome outcome, _) = await GeneratedChildBulk.RemoveAsync(
            dispatcher, remove, manufacturer.Value, RowVersion.Create(), [model.Value, Guid.CreateVersion7()]);

        // A stale answer ends the run: the second send is not made.
        Assert.Equal(0, outcome.Succeeded);
        Assert.Equal(1, outcome.Skipped);
    }

    private ChildViewModel ModelChild() =>
        Assert.Single(
            factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>().Children,
            child => child.Child == typeof(DeviceModel));

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();

        return await client.GetStringAsync(new Uri(path, UriKind.Relative));
    }

    private async Task<string> GetAsAsync(IUserContext caller, string path)
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(caller))));
        using HttpClient client = host.CreateClient();

        return await client.GetStringAsync(new Uri(path, UriKind.Relative));
    }

    private sealed class FixedUserContextFactory(IUserContext caller) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => caller;
    }
}
