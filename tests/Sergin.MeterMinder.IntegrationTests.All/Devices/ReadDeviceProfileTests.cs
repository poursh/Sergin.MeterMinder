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
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>The simulated ReadDeviceProfile command: runs in the background, changes nothing, fails as not found for a deleted device, needs its own permission and is schedulable.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ReadDeviceProfileTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task ReadProfile_SentInline_IsRefused()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);

        ErrorOr<Success> result = await dispatcher.SendAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(id)));

        Assert.Equal("ScheduledTasks.MustRunInBackground", result.FirstError.Code);
    }

    [Fact]
    public async Task ReadProfile_Started_SucceedsInTheBackground()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);

        var clock = Stopwatch.StartNew();
        ErrorOr<BackgroundRunStartedResponse> started = await dispatcher.StartAsync(
            new ReadDeviceProfileCommand(new DeviceIntenralId(id)), [new BackgroundTarget(id, null)]);
        clock.Stop();

        Assert.False(started.IsError, started.IsError ? started.FirstError.Description : string.Empty);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"start took {clock.Elapsed}");

        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await BackgroundRuns.WaitAsync(factory.Services, started.Value.Run, TimeSpan.FromSeconds(30));
        Assert.Equal("Completed", status);
        Assert.Equal("Succeeded", Assert.Single(items).Status);
    }

    [Fact]
    public async Task ReadProfile_LeavesTheRowVersionAlone()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        DeviceIntenralId id = new(await CreateDeviceAsync(dispatcher));

        ErrorOr<Versioned<DeviceQueryResponse>> before = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));
        ErrorOr<BackgroundRunStartedResponse> started = await dispatcher.StartAsync(
            new ReadDeviceProfileCommand(id), [new BackgroundTarget(id.Value, null)]);
        Assert.False(started.IsError);
        (string status, _) = await BackgroundRuns.WaitAsync(factory.Services, started.Value.Run, TimeSpan.FromSeconds(30));
        ErrorOr<Versioned<DeviceQueryResponse>> after = await dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(id));

        Assert.Equal("Completed", status);
        Assert.Equal(before.Value.Version, after.Value.Version);
    }

    [Fact]
    public async Task ReadProfile_OnADeletedDevice_FailsAsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);
        Assert.False((await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(new DeviceIntenralId(id)))).IsError);

        ErrorOr<BackgroundRunStartedResponse> started = await dispatcher.StartAsync(
            new ReadDeviceProfileCommand(new DeviceIntenralId(id)), [new BackgroundTarget(id, null)]);
        Assert.False(started.IsError, started.IsError ? started.FirstError.Description : string.Empty);
        (_, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await BackgroundRuns.WaitAsync(factory.Services, started.Value.Run, TimeSpan.FromSeconds(30));

        (string itemStatus, string? code) = Assert.Single(items);
        Assert.Equal("Failed", itemStatus);
        Assert.Equal(Error.NotFound().Code, code);
    }

    [Fact]
    public async Task ReadProfile_WithoutThePermission_IsForbiddenAtStart()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        Guid id = await CreateDeviceAsync(scope.ServiceProvider.GetRequiredService<ISerginDispatcher>());

        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller reader = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, RbacScenario.Read);

        ErrorOr<BackgroundRunStartedResponse> result = await tree.TrySendAsync(
            new StartBackgroundCommand(new ReadDeviceProfileCommand(new DeviceIntenralId(id)), [new BackgroundTarget(id, null)]), reader);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public void ReadProfile_IsSchedulable()
    {
        ScheduledCommandCatalog catalog = factory.Services.GetRequiredService<ScheduledCommandCatalog>();

        Assert.True(catalog.TryGet("dm.devices.read-profile", out ScheduledCommand? command));
        Assert.Equal(typeof(ReadDeviceProfileCommand), command.RequestType);
        Assert.Equal(TimeSpan.FromMinutes(10), command.Background!.Timeout);
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
