using System.Net;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Both device read models carry the model's name next to its id — DeviceQueryRepository joins
/// dm.device_model in both raw-SQL queries — so the list and detail pages show a name, not a Guid. The
/// manufacturer is deliberately absent from these read models (spec, Decision 4). Written through the real
/// command handlers and read back through ISerginDispatcher from a scope, the way a Blazor page does, so the
/// reads genuinely round-trip through Postgres. The last test renders the detail page itself:
/// OnParametersSetAsync runs during server-side prerendering, so the name is in the HTML a plain GET returns.
/// The list page is not rendered the same way on purpose — MudTable's ServerData loads after first render,
/// which prerendering never reaches, so its rows are absent from that HTML and the read-model assertion is
/// the honest check for it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceReadModelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetDeviceList_CarriesDeviceModelName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        DeviceId deviceId = NewDeviceId();
        await CreateDeviceAsync(dispatcher, deviceId, modelId);

        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        GetDeviceListItem item = Assert.Single(list.Value.Data, i => i.DeviceId == deviceId.Value);
        Assert.Equal(modelId.Value, item.DeviceModelId);
        Assert.Equal(modelName.Value, item.DeviceModelName);
    }

    [Fact]
    public async Task GetDeviceById_CarriesDeviceModelName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), modelId);

        ErrorOr<DeviceQueryResponse> device = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id));

        Assert.False(device.IsError, device.IsError ? device.FirstError.Description : string.Empty);
        Assert.Equal(modelId.Value, device.Value.DeviceModelId);
        Assert.Equal(modelName.Value, device.Value.DeviceModelName);
    }

    [Fact]
    public async Task DeviceDetailPage_RendersDeviceModelName_NotItsId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher);
        DeviceModelName modelName = NewDeviceModelName();
        DeviceModelInternalId modelId = await AddDeviceModelAsync(dispatcher, manufacturerId, modelName);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), modelId);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri($"/dm/devices/{id}", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Model: {modelName.Value}", html, StringComparison.Ordinal);
        // Neither the model's nor the manufacturer's id is visible text on this page.
        Assert.DoesNotContain(modelId.Value.ToString(), html, StringComparison.Ordinal);
        Assert.DoesNotContain(manufacturerId.Value.ToString(), html, StringComparison.Ordinal);
    }

    private static DeviceModelName NewDeviceModelName() => new($"model-{Guid.CreateVersion7()}");

    private static DeviceId NewDeviceId() => new($"device-{Guid.CreateVersion7()}");

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<DeviceModelInternalId> AddDeviceModelAsync(
        ISerginDispatcher dispatcher, ManufacturerId manufacturerId, DeviceModelName name)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAsync(
            new AddDeviceModelCommand(manufacturerId, name));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher, DeviceId deviceId, DeviceModelInternalId modelId)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(deviceId, modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
