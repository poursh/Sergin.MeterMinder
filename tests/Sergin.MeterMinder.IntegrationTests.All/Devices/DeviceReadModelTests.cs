using System.Net;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>
/// Both device read models carry the manufacturer's name next to its id — DeviceQueryRepository joins
/// dm.manufacturer in both raw-SQL queries — so the list and detail pages show a name, not a Guid.
/// Written through the real command handlers and read back through ISerginDispatcher from a scope, the
/// way a Blazor page does, so the reads genuinely round-trip through Postgres. The last test renders the
/// detail page itself: OnParametersSetAsync runs during server-side prerendering, so the name is in the
/// HTML a plain GET returns. The list page is not rendered the same way on purpose — MudTable's
/// ServerData loads after first render, which prerendering never reaches, so its rows are absent from
/// that HTML and the read-model assertion is the honest check for it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceReadModelTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task GetDeviceList_CarriesManufacturerName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerName name = NewManufacturerName();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, name);
        DeviceId deviceId = NewDeviceId();
        await CreateDeviceAsync(dispatcher, deviceId, manufacturerId);

        ErrorOr<ListQueryResponse<GetDeviceListItem>> list =
            await dispatcher.SendAsync(new GetDeviceListQueryCommand(Paggination.Create(1000, 1)));

        Assert.False(list.IsError, list.IsError ? list.FirstError.Description : string.Empty);
        GetDeviceListItem item = Assert.Single(list.Value.Data, i => i.DeviceId == deviceId.Value);
        Assert.Equal(manufacturerId.Value, item.ManufacturerId);
        Assert.Equal(name.Value, item.ManufacturerName);
    }

    [Fact]
    public async Task GetDeviceById_CarriesManufacturerName()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerName name = NewManufacturerName();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, name);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), manufacturerId);

        ErrorOr<DeviceQueryResponse> device = await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id));

        Assert.False(device.IsError, device.IsError ? device.FirstError.Description : string.Empty);
        Assert.Equal(manufacturerId.Value, device.Value.ManufacturerId);
        Assert.Equal(name.Value, device.Value.ManufacturerName);
    }

    [Fact]
    public async Task DeviceDetailPage_RendersManufacturerName_NotItsId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerName name = NewManufacturerName();
        ManufacturerId manufacturerId = await CreateManufacturerAsync(dispatcher, name);
        Guid id = await CreateDeviceAsync(dispatcher, NewDeviceId(), manufacturerId);

        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri($"/dm/devices/{id}", UriKind.Relative));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(name.Value, html, StringComparison.Ordinal);
        // The id survives only as the link target to the manufacturer's page, never as visible text.
        Assert.Contains($"/dm/manufacturers/{manufacturerId.Value}", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"Manufacturer: {manufacturerId.Value}", html, StringComparison.Ordinal);
    }

    private static ManufacturerName NewManufacturerName() => new($"manufacturer-{Guid.CreateVersion7()}");

    private static DeviceId NewDeviceId() => new($"device-{Guid.CreateVersion7()}");

    private static async Task<ManufacturerId> CreateManufacturerAsync(ISerginDispatcher dispatcher, ManufacturerName name)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(name, Address: null));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher, DeviceId deviceId, ManufacturerId manufacturerId)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(deviceId, manufacturerId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
