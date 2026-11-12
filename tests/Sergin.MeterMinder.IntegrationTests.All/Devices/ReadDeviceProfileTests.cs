using System.Diagnostics;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>The simulated ReadDeviceProfile command: waits, changes nothing, is not found for a deleted device, needs its own permission and is schedulable.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ReadDeviceProfileTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task ReadProfile_OnALiveDevice_SucceedsAfterTheDelay()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);

        var clock = Stopwatch.StartNew();
        ErrorOr<Success> result = await dispatcher.SendAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(id)));
        clock.Stop();

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(3) - TimeSpan.FromMilliseconds(100), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task ReadProfile_LeavesTheRowVersionAlone()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceIntenralId id = new(await CreateDeviceAsync(dispatcher));

        ErrorOr<Versioned<DeviceQueryResponse>> before = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));
        Assert.False((await dispatcher.SendAsync(new ReadDeviceProfileCommand(id))).IsError);
        ErrorOr<Versioned<DeviceQueryResponse>> after = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));

        Assert.Equal(before.Value.Version, after.Value.Version);
    }

    [Fact]
    public async Task ReadProfile_OnADeletedDevice_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        Assert.False((await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(new DeviceIntenralId(id)))).IsError);

        ErrorOr<Success> result = await dispatcher.SendAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(id)));

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task ReadProfile_WithoutThePermission_IsForbidden()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Guid id = await CreateDeviceAsync(scope.ServiceProvider.GetRequiredService<ISerginDispatcher>());

        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller reader = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, RbacScenario.Read);

        ErrorOr<Success> result = await tree.TrySendAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(id)), reader);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public void ReadProfile_IsSchedulable()
    {
        ScheduledCommandCatalog catalog = factory.Services.GetRequiredService<ScheduledCommandCatalog>();

        Assert.True(catalog.TryGet("dm.devices.read-profile", out ScheduledCommand? command));
        Assert.Equal(typeof(ReadDeviceProfileCommand), command.RequestType);
    }

    private static async Task<Guid> CreateDeviceAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<CreateManufacturerCommandResponse> manufacturer = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(manufacturer.IsError);
        ManufacturerId manufacturerId = new(manufacturer.Value.Id);

        ErrorOr<AddDeviceModelCommandResponse> model = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));
        Assert.False(model.IsError);

        ErrorOr<CreateDeviceCommandResponse> device = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(model.Value.Id)));
        Assert.False(device.IsError);
        return device.Value.Id;
    }
}
