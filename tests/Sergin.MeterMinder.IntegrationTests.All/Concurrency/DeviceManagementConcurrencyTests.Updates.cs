using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

public sealed partial class DeviceManagementConcurrencyTests
{
    [Fact]
    public async Task UpdateDevice_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        DeviceQueryResponse device = (await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id))).Value;

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateDeviceCommand(id, new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(device.DeviceModelId)));

        Assert.Equal(VersionErrors.RequiredType, (int)updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateDevice_WithAStaleVersion_IsRefused_AndNothingIsSaved()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        Guid other = await CreateDeviceAsync(dispatcher);
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));
        ErrorOr<Versioned<DeviceQueryResponse>> otherLoaded = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(other));

        ErrorOr<Versioned<UpdateDeviceCommandResponse>> updated = await dispatcher.SendVersionedAsync(
            new UpdateDeviceCommand(id, new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(loaded.Value.Value.DeviceModelId)),
            otherLoaded.Value.Version);

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.Equal(loaded.Value.Value.DeviceId, (await dispatcher.SendAsync(new GetDeviceByIdQueryCommand(id))).Value.DeviceId);
    }

    [Fact]
    public async Task UpdateManufacturer_WithoutAVersion_IsRefusedWith428()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);

        ErrorOr<UpdateManufacturerCommandResponse> updated = await dispatcher.SendAsync(
            new UpdateManufacturerCommand(id.Value, new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));

        Assert.Equal(VersionErrors.RequiredType, (int)updated.FirstError.Type);
    }

    [Fact]
    public async Task UpdateManufacturer_WithAStaleVersion_IsRefused_AndNothingIsSaved()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId id = await CreateManufacturerAsync(dispatcher);
        ManufacturerId other = await CreateManufacturerAsync(dispatcher);
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded = await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(id.Value));
        ErrorOr<Versioned<ManufacturerQueryResponse>> otherLoaded = await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(other.Value));

        ErrorOr<Versioned<UpdateManufacturerCommandResponse>> updated = await dispatcher.SendVersionedAsync(
            new UpdateManufacturerCommand(id.Value, new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null),
            otherLoaded.Value.Version);

        Assert.True(VersionErrors.IsStale(updated.FirstError));
        Assert.Equal(loaded.Value.Value.Name, (await dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(id.Value))).Value.Name);
    }
}
