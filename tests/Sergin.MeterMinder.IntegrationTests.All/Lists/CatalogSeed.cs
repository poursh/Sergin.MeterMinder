using ErrorOr;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Lists;

/// <summary>
/// Writes a manufacturer, its models and devices through the real handlers, the way a Blazor page does, with names
/// carrying a per-test marker so a test can find its own rows among everything the shared database holds.
/// </summary>
internal static class CatalogSeed
{
    public static string Marker() => Guid.CreateVersion7().ToString("N")[..12];

    public static async Task<ManufacturerId> ManufacturerAsync(ISerginDispatcher dispatcher, string name, string? address = null)
    {
        ErrorOr<CreateManufacturerCommandResponse> created = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName(name), address is null ? null : new ManufacturerAddress(address)));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new ManufacturerId(created.Value.Id);
    }

    public static async Task<DeviceModelInternalId> ModelAsync(ISerginDispatcher dispatcher, ManufacturerId manufacturerId, string name)
    {
        ErrorOr<AddDeviceModelCommandResponse> added = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName(name)));

        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Id);
    }

    public static async Task<Guid> DeviceAsync(ISerginDispatcher dispatcher, string deviceId, DeviceModelInternalId modelId)
    {
        ErrorOr<CreateDeviceCommandResponse> created = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(deviceId), modelId));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }
}
