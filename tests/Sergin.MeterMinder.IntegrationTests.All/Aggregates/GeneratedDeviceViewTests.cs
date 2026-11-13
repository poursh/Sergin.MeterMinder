using System.Net;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Validation;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>How many times the device overview query went through the MediatR pipeline.</summary>
internal sealed class OverviewSendCounter
{
    private int sends;

    public int Sends => Volatile.Read(ref sends);

    public void Count() => Interlocked.Increment(ref sends);
}

internal sealed class OverviewCountingBehavior<TRequest, TResponse>(OverviewSendCounter counter) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is GetDeviceOverviewQueryCommand)
        {
            counter.Count();
        }

        return next(cancellationToken);
    }
}

/// <summary>
/// What the generated Device views send, checked at the dispatcher (a component cannot be driven server-side), and what the
/// generated list prerenders.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedDeviceViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task DeviceDetail_StaleDelete_ReloadsAndKeepsDevice()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        CommandView delete = Assert.Single(model.Commands, command => command.Request == typeof(DeleteDeviceCommand));
        Assert.True(delete.RequiresVersion);

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId deviceModel = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", deviceModel);

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> stale =
            await dispatcher.SendVersionedAsync(new DeleteDeviceCommand(new DeviceIntenralId(device)), RowVersion.Create());

        Assert.True(stale.IsError);
        Assert.Equal(VersionErrors.Stale.Code, stale.FirstError.Code);
        Assert.False((await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(new DeviceIntenralId(device)))).IsError);
    }

    [Fact]
    public async Task DeviceList_OverviewQuery_SentOncePerLoad()
    {
        OverviewSendCounter counter = new();
        using WebApplicationFactory<Program> counted = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(counter);
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OverviewCountingBehavior<,>));
        }));
        using HttpClient client = counted.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/dm/devices", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, counter.Sends);

        // The slot got what the view loaded: "of {Total}" shows only for a loaded overview.
        string html = await response.Content.ReadAsStringAsync();
        Assert.Matches(@"of \d+", html);
    }

    [Fact]
    public async Task DeviceList_RendersColumnsSortLabelsFilterAndBulk()
    {
        string html = await GetAsync("/dm/devices");

        Assert.Contains("Device ID", html, StringComparison.Ordinal);
        Assert.Contains("Model", html, StringComparison.Ordinal);
        Assert.Contains("Manufacturer", html, StringComparison.Ordinal);
        Assert.Contains("mud-table-sort-label", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Advanced filter\"", html, StringComparison.Ordinal);
        Assert.Contains("New device", html, StringComparison.Ordinal);
        Assert.Contains("mud-checkbox", html, StringComparison.Ordinal);
        Assert.Contains("Search device ID or model", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceList_BulkSchedule_TargetsEverySelectedRow()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        CommandView readProfile = Assert.Single(model.Commands, command => command.Request == typeof(ReadDeviceProfileCommand));
        Assert.True(readProfile.Selectable);
        Assert.NotNull(readProfile.ScheduleName);

        GetDeviceListItem[] items =
        [
            new(Guid.CreateVersion7(), "dev-a", Guid.CreateVersion7(), "model", Guid.CreateVersion7(), "maker"),
            new(Guid.CreateVersion7(), "dev-b", Guid.CreateVersion7(), "model", Guid.CreateVersion7(), "maker"),
            new(Guid.CreateVersion7(), "dev-c", Guid.CreateVersion7(), "model", Guid.CreateVersion7(), "maker"),
        ];

        SerginScheduleRequest request = GeneratedListBody<GetDeviceListItem>.ScheduleRequestFor(model, readProfile, items);

        Assert.Equal(model.Schema, request.Schema);
        Assert.Equal(readProfile.Label, request.CommandTitle);
        Assert.Equal(items.Select(item => item.Id), request.Targets.Select(target => target.TargetId));
        Assert.Equal(["dev-a", "dev-b", "dev-c"], request.TargetLabels);
        Assert.IsType<ReadDeviceProfileCommand>(request.BuildTemplate());
    }

    [Fact]
    public void DeviceList_ItemAndBodyType_AreResolvedAtRegistryBuild()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();

        Assert.Equal(typeof(GetDeviceListItem), model.ListItem);
        Assert.Equal(typeof(GeneratedListBody<GetDeviceListItem>), model.ListBodyType);
        Assert.Equal(nameof(GetDeviceListItem.DeviceId), model.ListTitleField);
    }

    [Fact]
    public async Task DeviceList_OverviewSlot_Prerenders()
    {
        string html = await GetAsync("/dm/devices");

        Assert.Contains("aria-label=\"Overview\"", html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.OverviewDevices.Default, html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.OverviewModelsInUse.Default, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceCreate_RendersGeneratedInputs()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        Assert.Equal(DeviceUiTexts.New, model.CreateTitle);
        FormFieldView picker = Assert.Single(model.CreateFields, field => field.Kind == FormInputKind.ChildPicker);
        Assert.NotNull(picker.ParentOptions);
        Assert.NotNull(picker.Options);

        string html = await GetAsync("/dm/devices/new");

        Assert.Contains("Device ID", html, StringComparison.Ordinal);
        Assert.Contains("Manufacturer", html, StringComparison.Ordinal);
        Assert.Contains("Model", html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.SelectManufacturerFirst.Default, html, StringComparison.Ordinal);
        Assert.Contains("mud-disabled", html, StringComparison.Ordinal);
        Assert.Contains(">Create<", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/dm/devices\"", html, StringComparison.Ordinal);
        Assert.Contains("New device", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceEdit_PrefillsFromRecord()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        Assert.Equal(DeviceUiTexts.EditTitle, model.UpdateTitle);

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId deviceModel = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", deviceModel);

        string html = await GetAsync($"/dm/devices/{device}/edit");

        Assert.Contains($"value=\"dev-{marker}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Save<", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/dm/devices/{device}\"", html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.EditTitle.Default, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceEdit_StaleSave_ReloadsForm()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();
        CommandView update = Assert.Single(model.Commands, command => command.Request == typeof(UpdateDeviceCommand));
        Assert.True(update.RequiresVersion);

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId deviceModel = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", deviceModel);

        Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            [nameof(UpdateDeviceCommand.DeviceId)] = $"renamed-{marker}",
            [nameof(UpdateDeviceCommand.DeviceModelId)] = deviceModel.Value,
        };

        ViewSendResult stale = await model.Update!.Invoker.SendVersionedAsync(dispatcher, model.Update.Build(device, values), RowVersion.Create());

        Assert.True(stale.IsError);
        Assert.Equal(VersionErrors.Stale.Code, stale.Errors[0].Code);

        ErrorOr<Versioned<DeviceQueryResponse>> reloaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(new DeviceIntenralId(device)));
        Assert.Equal($"dev-{marker}", reloaded.Value.Value.DeviceId);
    }

    [Fact]
    public async Task DeviceCreate_DuplicateId_ValidationMessageFromPipeline()
    {
        AggregateViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();

        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ISerginFormValidator validator = scope.ServiceProvider.GetRequiredService<ISerginFormValidator>();
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId deviceModel = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", deviceModel);

        Dictionary<string, object?> values = new(StringComparer.Ordinal)
        {
            [nameof(CreateDeviceCommand.DeviceId)] = $"dev-{marker}",
            [nameof(CreateDeviceCommand.DeviceModelId)] = deviceModel.Value,
        };

        IReadOnlyCollection<string> messages = await model.Create!.Invoker.ValidateAsync(
            validator, model.Create.Build(Guid.Empty, values), nameof(CreateDeviceCommand.DeviceId));

        Assert.Contains("'Device ID' is already in use.", messages);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
