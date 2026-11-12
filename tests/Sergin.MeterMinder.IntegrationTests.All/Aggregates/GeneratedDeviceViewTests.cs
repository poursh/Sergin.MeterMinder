using System.Net;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
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

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

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
    public async Task DeviceList_OverviewSlot_Prerenders()
    {
        string html = await GetAsync("/dm/devices");

        Assert.Contains("aria-label=\"Overview\"", html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.OverviewDevices.Default, html, StringComparison.Ordinal);
        Assert.Contains(DeviceUiTexts.OverviewModelsInUse.Default, html, StringComparison.Ordinal);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
