using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
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
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
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
    public async Task SendFromId_StoresTheRecordTitleAsLabel()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid device = (await DevicesAsync(dispatcher, 1))[0];

        ViewSendResult result = await GeneratedViewSupport.SendFromIdAsync(dispatcher, ReadProfile(), device, version: null, label: "DEV-TITLE");

        BackgroundRunStartedResponse started = Assert.IsType<BackgroundRunStartedResponse>(result.Value);
        Assert.Equal(["DEV-TITLE"], await LabelsAsync(started.Run));
    }

    [Fact]
    public async Task StartMany_StoresEachRowsTitle()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        IReadOnlyList<Guid> devices = await DevicesAsync(dispatcher, 3);

        ViewSendResult result = await GeneratedViewSupport.StartManyAsync(
            dispatcher, ReadProfile(), [.. devices.Select(device => new BackgroundTarget(device, null, "t-" + device))]);

        BackgroundRunStartedResponse started = Assert.IsType<BackgroundRunStartedResponse>(result.Value);
        Assert.Equal(devices.Select(device => "t-" + device).Order(), (await LabelsAsync(started.Run)).Order());
    }

    [Fact]
    public async Task StartMany_OneRunForAllRows()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        IReadOnlyList<Guid> devices = await DevicesAsync(dispatcher, 3);

        ViewSendResult result = await GeneratedViewSupport.StartManyAsync(
            dispatcher, ReadProfile(), [.. devices.Select(device => new BackgroundTarget(device, null, "t-" + device))]);

        Assert.False(result.IsError, result.IsError ? result.Errors[0].Description : string.Empty);
        BackgroundRunStartedResponse started = Assert.IsType<BackgroundRunStartedResponse>(result.Value);
        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await BackgroundRuns.WaitAsync(factory.Services, started.Run, RunTimeout);
        Assert.Equal("Completed", status);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal("Succeeded", item.Status));
    }

    [Fact]
    public async Task DetailAction_ShowsASpinner_WhileItsRunIsUnfinished()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Guid device = (await DevicesAsync(scope.ServiceProvider.GetRequiredService<ISerginDispatcher>(), 1))[0];
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/dm/devices/{device}");
        ActivityPanelState activity = scope.ServiceProvider.GetRequiredService<ActivityPanelState>();
        string commandName = ReadProfile().ScheduleName!;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<DetailHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DetailHost.Id)] = device }));
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

            Assert.Contains("Read profile", Html(), StringComparison.Ordinal);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            // A run of the command on another record, and a finished one on this record, show nothing.
            activity.SetRows([Row(commandName, Guid.CreateVersion7(), ActivityState.Running), Row(commandName, device, ActivityState.Succeeded)]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            activity.SetRows([Row(commandName, device, ActivityState.Running)]);
            Assert.Contains(Spinner, Html(), StringComparison.Ordinal);

            // The action stays clickable while its run is going.
            Assert.DoesNotMatch(@"<button[^>]*disabled[^>]*>\s*(<[^>]*>\s*)*Read profile", Html());

            activity.SetRows([]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
        });
    }

    private const string Spinner = "sergin-action-busy";

    private static ActivityRow Row(string commandName, Guid target, ActivityState state) =>
        new(new BackgroundRunRef("dm", Guid.CreateVersion7()), null, commandName, "DEV", null, state, DateTime.UtcNow, null, 0, 1, null, null, target, null, null, 0);

    /// <summary>The popover provider the split button's menu renders into, and the device's generated detail view.</summary>
    private sealed class DetailHost : ComponentBase
    {
        [Parameter]
        public Guid Id { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<SerginGeneratedDetailView<Device>>(1);
            builder.AddComponentParameter(2, nameof(SerginGeneratedDetailView<Device>.Id), Id);
            builder.CloseComponent();
        }
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

    private async Task<IReadOnlyList<string?>> LabelsAsync(BackgroundRunRef run)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT target_label FROM dm.scheduled_task_run_items WHERE run_id = @id";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = run.RunId;
        command.Parameters.Add(parameter);
        List<string?> labels = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            labels.Add(await reader.IsDBNullAsync(0) ? null : reader.GetString(0));
        }

        return labels;
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
