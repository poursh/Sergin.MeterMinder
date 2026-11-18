using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Runtime;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>A generated view starts a background command (Read profile) as a run instead of sending it inline.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedBackgroundCommandTests(SerginWebApiFactory<Program> factory)
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void ReadProfile_CommandView_IsBackground()
    {
        Assert.NotNull(ReadProfile().Background);
    }

    [Fact]
    public void BackgroundTrigger_HasItsOwnText()
    {
        Assert.Equal(ScheduledTaskUiTexts.Runs.Background, ScheduledTaskUiFormat.TriggerText(ScheduledRunTrigger.Background));
        Assert.Equal(ScheduledTaskUiTexts.Runs.OneOff, ScheduledTaskUiFormat.TriggerText(ScheduledRunTrigger.OneOff));
    }

    [Fact]
    public async Task SendFromId_OnABackgroundCommand_StartsARun()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid device = (await DevicesAsync(dispatcher, 1))[0];

        ViewSendResult result = await GeneratedViewSupport.SendFromIdAsync(dispatcher, ReadProfile(), device, version: null);

        Assert.False(result.IsError, result.IsError ? result.Errors[0].Description : string.Empty);
        Assert.Null(result.Version);
        BackgroundRunStartedResponse started = Assert.IsType<BackgroundRunStartedResponse>(result.Value);
        Assert.Equal("dm", started.Run.Schema);
        Assert.Equal("Background", await TriggerOfAsync(started.Run));

        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await BackgroundRuns.WaitAsync(factory.Services, started.Run, RunTimeout);
        Assert.Equal("Completed", status);
        Assert.Equal("Succeeded", Assert.Single(items).Status);
    }

    [Fact]
    public async Task StartMany_OneRunForAllRows()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        IReadOnlyList<Guid> devices = await DevicesAsync(dispatcher, 3);

        ViewSendResult result = await GeneratedViewSupport.StartManyAsync(
            dispatcher, ReadProfile(), [.. devices.Select(device => new ScheduledTaskTarget(device, null))]);

        Assert.False(result.IsError, result.IsError ? result.Errors[0].Description : string.Empty);
        BackgroundRunStartedResponse started = Assert.IsType<BackgroundRunStartedResponse>(result.Value);
        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await BackgroundRuns.WaitAsync(factory.Services, started.Run, RunTimeout);
        Assert.Equal("Completed", status);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal("Succeeded", item.Status));
    }

    private CommandView ReadProfile() =>
        Assert.Single(
            factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>().Commands,
            command => command.Request == typeof(ReadDeviceProfileCommand));

    private static async Task<IReadOnlyList<Guid>> DevicesAsync(ISerginDispatcher dispatcher, int count)
    {
        string marker = CatalogSeed.Marker();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        List<Guid> devices = [];

        for (int index = 0; index < count; index++)
        {
            devices.Add(await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}-{index}", model));
        }

        return devices;
    }

    private async Task<string?> TriggerOfAsync(BackgroundRunRef run)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """SELECT "trigger" FROM dm.scheduled_task_runs WHERE id = @id""";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = run.RunId;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync() as string;
    }
}
