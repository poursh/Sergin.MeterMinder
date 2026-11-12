using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Devices;

/// <summary>Read profile as a scheduled command, end to end through the real host's in-process stand-in engine.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceReadProfileScheduleTests(SerginWebApiFactory<Program> factory)
{
    private const string Schema = "dm";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task OneOffReadProfile_OnTwoDevices_BothSucceed()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid first = await CreateDeviceAsync(dispatcher);
        Guid second = await CreateDeviceAsync(dispatcher);

        ErrorOr<ScheduledRunStartedResponse> started = await dispatcher.SendAsync(new RunCommandOnceCommand(
            Schema, new ReadDeviceProfileCommand(new DeviceIntenralId(Guid.Empty)), [new(first, null), new(second, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRun run = await CompletedRunAsync(started.Value.RunId);

        Assert.Equal(2, run.Succeeded);
        IReadOnlyCollection<ScheduledTaskRunItem> items = await ItemsAsync(run.Id);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status));
    }

    [Fact]
    public async Task ScheduledReadProfile_OnADeletedDevice_FailsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid id = await CreateDeviceAsync(dispatcher);

        ErrorOr<CreateScheduledTaskCommandResponse> created = await dispatcher.SendAsync(new CreateScheduledTaskCommand(
            Schema, new ReadDeviceProfileCommand(new DeviceIntenralId(Guid.Empty)), [new(id, null)], "0 3 * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        Assert.False((await dispatcher.SendAtDeviceVersionAsync(id, new DeleteDeviceCommand(new DeviceIntenralId(id)))).IsError);

        ErrorOr<ScheduledRunStartedResponse> started = await dispatcher.SendAsync(
            new RunScheduledTaskNowCommand(Schema, new ScheduledTaskId(created.Value.Id)));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRun run = await CompletedRunAsync(started.Value.RunId);

        Assert.Equal(1, run.Failed);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal("General.NotFound", item.ErrorCode);
    }

    private static IScheduledTasksDbContext Tasks(IServiceScope scope) =>
        Assert.IsAssignableFrom<IScheduledTasksDbContext>(scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>());

    private async Task<ScheduledTaskRun> CompletedRunAsync(Guid runId)
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = factory.Services.CreateScope();

            if (await Tasks(scope).ScheduledTaskRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == runId && run.Status == ScheduledRunStatus.Completed) is { } found)
            {
                return found;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The run did not complete.");
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await Tasks(scope).ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
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
