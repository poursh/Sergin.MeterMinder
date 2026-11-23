using System.Data.Common;
using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskViewProbes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Lists;
using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Actions;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Runtime;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Scopes;
using Sergin.SharedKernel.Presentation.Blazor.Tasks;
using Sergin.SharedKernel.Presentation.Blazor.TimeZones;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// Generated views place tasks by type: a task over one record (Read profile, the probe's Ping and Sweep) is a detail and
/// row action started as a run over the record, with Schedule… and View schedules only when it is <c>Schedulable()</c>; a
/// task over the collection (the probe's Refresh) is a list header action started as one run with no record in the current
/// scope, with a spinner while that run is going, and Schedule… and a Schedules dialog when schedulable; no other command
/// gets either.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedTaskViewTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Spinner = "sergin-action-busy";
    private const string RefreshName = "tst.task-view-probes.refresh";

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;
    private TaskViewProbeStore store = default!;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            ProbeHandlers.AddTo(services);
            ScheduleProbeUiModule.AddTo(services);
            ProbeUserContextFactory.AddTo(services);
            TaskViewProbeTestHost.AddTo(services);
            services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
        }));

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        store = host.Services.GetRequiredService<TaskViewProbeStore>();
        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    // ---- The device's Read profile, on the real host ----

    [Fact]
    public void ReadProfile_CommandView_IsATaskOverOneRecord()
    {
        CommandView readProfile = ReadProfile();

        Assert.Equal(CommandRole.Task, readProfile.Role);
        Assert.NotNull(readProfile.Task);
    }

    [Fact]
    public void ManualTrigger_HasItsOwnText()
    {
        Assert.Equal(TaskUiTexts.Runs.Manual, TaskUiFormat.TriggerText(TaskRunTrigger.Manual));
        Assert.Equal(TaskUiTexts.Runs.OneOff, TaskUiFormat.TriggerText(TaskRunTrigger.Once));
    }

    [Fact]
    public async Task SendFromId_OnATask_StartsARun()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid device = (await DevicesAsync(dispatcher, 1))[0];

        ViewSendResult result = await GeneratedViewSupport.SendFromIdAsync(dispatcher, ReadProfile(), device, version: null);

        Assert.False(result.IsError, result.IsError ? result.Errors[0].Description : string.Empty);
        Assert.Null(result.Version);
        TaskRunStartedResponse started = Assert.IsType<TaskRunStartedResponse>(result.Value);
        Assert.Equal("dm", started.Run.Schema);
        Assert.Equal("Manual", await ScalarAsync(factory, """SELECT "trigger" FROM dm.task_runs WHERE id = @id""", started.Run.RunId));

        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await TaskRunReader.WaitAsync(factory.Services, started.Run, RunTimeout);
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

        TaskRunStartedResponse started = Assert.IsType<TaskRunStartedResponse>(result.Value);
        Assert.Equal(["DEV-TITLE"], await LabelsAsync(started.Run));
    }

    [Fact]
    public async Task StartMany_OneRunForAllRows_EachRowsTitleStored()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        IReadOnlyList<Guid> devices = await DevicesAsync(dispatcher, 3);

        ViewSendResult result = await GeneratedViewSupport.StartManyAsync(
            dispatcher, ReadProfile(), [.. devices.Select(device => new TaskTarget(device, "t-" + device))]);

        Assert.False(result.IsError, result.IsError ? result.Errors[0].Description : string.Empty);
        TaskRunStartedResponse started = Assert.IsType<TaskRunStartedResponse>(result.Value);
        Assert.Equal(devices.Select(device => "t-" + device).Order(), (await LabelsAsync(started.Run)).Order());
        (string status, IReadOnlyList<(string Status, string? ErrorCode)> items) =
            await TaskRunReader.WaitAsync(factory.Services, started.Run, RunTimeout);
        Assert.Equal("Completed", status);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal("Succeeded", item.Status));
    }

    [Fact]
    public async Task ReadProfile_DetailAction_ShowsASpinner_WhileItsRunIsUnfinished()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Guid device = (await DevicesAsync(scope.ServiceProvider.GetRequiredService<ISerginDispatcher>(), 1))[0];
        ActivityPanelState activity = scope.ServiceProvider.GetRequiredService<ActivityPanelState>();
        string commandName = ReadProfile().Task!.Name;

        await GeneratedViewRendering.RenderAsync<SerginGeneratedDetailView<Device>>(scope, $"/dm/devices/{device}", Id(device), (root, _) =>
        {
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

            Assert.Contains("Read profile", Html(), StringComparison.Ordinal);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            // A run of the command on another record, and a finished one on this record, show nothing.
            activity.SetRows([EntityRow(commandName, Guid.CreateVersion7(), RunOutcome.Running), EntityRow(commandName, device, RunOutcome.Succeeded)]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            activity.SetRows([EntityRow(commandName, device, RunOutcome.Running)]);
            Assert.Contains(Spinner, Html(), StringComparison.Ordinal);

            // The action stays clickable while its run is going.
            Assert.DoesNotMatch(@"<button[^>]*disabled[^>]*>\s*(<[^>]*>\s*)*Read profile", Html());

            activity.SetRows([]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    // ---- The probe, on the tasks test host ----

    [Fact]
    public async Task EntityTask_NotSchedulable_HasNoScheduleEntry_ButViewsSchedules()
    {
        Guid probe = store.Add("Probe ping");
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);

        await RenderProbeAsync<SerginGeneratedDetailView<TaskViewProbe>>($"/tst/task-view-probes/{probe}", Id(probe), (_, view, _) =>
        {
            SerginPageAction ping = ActionOf(((SerginGeneratedDetailView<TaskViewProbe>)view).Actions, TaskViewProbeTexts.Ping);

            Assert.Equal([TaskUiTexts.ViewSchedules], ping.MenuItems!.Select(item => item.Label));
            Assert.Null(ping.Href);
            Assert.Equal(CommandRole.Task, CommandOf(typeof(PingTaskViewProbeCommand)).Role);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EntityTask_NoLongerSchedulable_ItsSavedScheduleStaysRemovable()
    {
        Guid probe = store.Add("Probe once schedulable");
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);

        // A schedule saved while the task was Schedulable(): saved as Sweep, then renamed to Ping, which is not.
        await CreateScheduleAsync(new SweepTaskViewProbeCommand(new TaskViewProbeId(probe)), [new TaskScheduleTarget(probe)], "41 5 * * *");
        await ScalarAsync(host, "UPDATE tst.task_schedules SET command_name = 'tst.task-view-probes.ping' WHERE command_name = 'tst.task-view-probes.sweep'");

        await RenderProbeAsync<SerginGeneratedDetailView<TaskViewProbe>>($"/tst/task-view-probes/{probe}", Id(probe), (root, view, _) =>
        {
            string html = WebUtility.HtmlDecode(root.ToHtmlString());
            SerginPageAction ping = ActionOf(((SerginGeneratedDetailView<TaskViewProbe>)view).Actions, TaskViewProbeTexts.Ping);

            // The section lists the schedule with its Delete (and Pause); Schedule… is not offered for the task.
            Assert.Contains(SerginTaskSchedulesSection.ElementId, html, StringComparison.Ordinal);
            Assert.Contains("41 5 * * *", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Delete\"", html, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Pause\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain(ping.MenuItems!, item => item.Label == TaskUiTexts.ScheduleAction);
            Assert.Contains(ping.MenuItems!, item => item.Label == TaskUiTexts.ViewSchedules);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EntityTask_Schedulable_HasScheduleAndViewSchedules()
    {
        Guid probe = store.Add("Probe sweep");
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);

        await RenderProbeAsync<SerginGeneratedDetailView<TaskViewProbe>>($"/tst/task-view-probes/{probe}", Id(probe), (root, view, _) =>
        {
            IReadOnlyList<SerginPageAction> actions = ((SerginGeneratedDetailView<TaskViewProbe>)view).Actions;
            SerginPageAction sweep = ActionOf(actions, TaskViewProbeTexts.Sweep);

            Assert.Equal([TaskUiTexts.ScheduleAction, TaskUiTexts.ViewSchedules], sweep.MenuItems!.Select(item => item.Label));
            Assert.Contains("Scheduled tasks", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            // A command that is not a task gets neither the schedule entries nor a spinner, and the collection task is not a
            // record's action.
            SerginPageAction retitle = ActionOf(actions, TaskViewProbeTexts.Retitle);
            Assert.Null(retitle.MenuItems);
            Assert.False(retitle.Busy);
            Assert.DoesNotContain(actions, action => action.Label == TaskViewProbeTexts.Refresh);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CollectionTask_RendersInListHeader_AndStartsOneCollectionRun()
    {
        store.Add("Probe listed");
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], async (root, view, _) =>
        {
            var list = (SerginGeneratedListView<TaskViewProbe>)view;
            Assert.Contains("Refresh probes", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            SerginPageAction refresh = ActionOf(list.Actions, TaskViewProbeTexts.Refresh);
            Assert.Equal([TaskUiTexts.ScheduleAction, TaskUiTexts.Schedules], refresh.MenuItems!.Select(item => item.Label));
            Assert.Null(refresh.Href);
            Assert.False(refresh.Busy);

            // The record tasks and the transition are the rows', never the header's.
            Assert.DoesNotContain(list.Actions, action => action.Label == TaskViewProbeTexts.Ping || action.Label == TaskViewProbeTexts.Retitle);

            await refresh.OnClick.InvokeAsync();
        });

        Assert.Equal(1L, await ScalarAsync(host, $"SELECT count(*) FROM tst.task_runs WHERE command_name = '{RefreshName}' AND target_kind = 'Collection'"));
        Assert.Equal(
            1L,
            await ScalarAsync(host, $"SELECT count(*) FROM tst.task_run_items i JOIN tst.task_runs r ON r.id = i.run_id WHERE r.command_name = '{RefreshName}' AND i.target_id IS NULL"));
    }

    [Fact]
    public async Task CollectionTask_WithoutManage_IsAPlainButton()
    {
        await GrantAsync(TaskViewProbePermissions.Run);

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], (_, view, _) =>
        {
            Assert.Null(ActionOf(((SerginGeneratedListView<TaskViewProbe>)view).Actions, TaskViewProbeTexts.Refresh).MenuItems);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CollectionTask_Running_ShowsSpinner()
    {
        await GrantAsync(TaskViewProbePermissions.Run);

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], (root, view, services) =>
        {
            ActivityPanelState activity = services.GetRequiredService<ActivityPanelState>();
            Guid current = services.GetRequiredService<IAuthorizationScopeContext>().ScopeId;
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
            bool Busy() => ActionOf(((SerginGeneratedListView<TaskViewProbe>)view).Actions, TaskViewProbeTexts.Refresh).Busy;

            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            // Its run in another scope, a finished run here, and a record's run of the same name show nothing.
            activity.SetRows(
            [
                CollectionRow(RefreshName, Guid.CreateVersion7(), RunOutcome.Running),
                CollectionRow(RefreshName, current, RunOutcome.Succeeded),
                EntityRow(RefreshName, Guid.CreateVersion7(), RunOutcome.Running),
            ]);
            Assert.False(Busy());
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);

            activity.SetRows([CollectionRow(RefreshName, current, RunOutcome.Running)]);
            Assert.True(Busy());
            Assert.Contains(Spinner, Html(), StringComparison.Ordinal);
            Assert.DoesNotMatch(@"<button[^>]*disabled[^>]*>\s*(<[^>]*>\s*)*Refresh probes", Html());

            activity.SetRows([]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CollectionTask_SecondClick_NotifiesAlreadyRunning()
    {
        await GrantAsync(TaskViewProbePermissions.Run);
        string scopeName = await RootScopeNameAsync();

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], async (_, view, services) =>
        {
            var list = (SerginGeneratedListView<TaskViewProbe>)view;
            CommandView refresh = CommandOf(typeof(RefreshTaskViewProbesCommand));
            ISnackbar snackbar = services.GetRequiredService<ISnackbar>();

            // The fake engine runs nothing, so the first run stays Running and the second start is refused.
            await list.StartAsync(refresh);
            await list.StartAsync(refresh);

            Snackbar refused = Assert.Single(snackbar.ShownSnackbars, shown => shown.Severity != Severity.Info);
            Assert.Equal($"Refresh probes is already running in {scopeName}.", refused.Message);
            Assert.Single(snackbar.ShownSnackbars, shown => shown.Severity == Severity.Info);
        });

        Assert.Equal(1L, await ScalarAsync(host, $"SELECT count(*) FROM tst.task_runs WHERE command_name = '{RefreshName}'"));
    }

    [Fact]
    public async Task SchedulesMenu_OpensDialogListingCollectionSchedules()
    {
        Guid probe = store.Add("Probe scheduled");
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);
        string scopeName = await RootScopeNameAsync();
        await CreateScheduleAsync(new RefreshTaskViewProbesCommand(), [], "17 3 * * *");
        await CreateScheduleAsync(new SweepTaskViewProbeCommand(new TaskViewProbeId(probe)), [new TaskScheduleTarget(probe)], "29 4 * * *");

        Guid current = Guid.Empty;

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], async (_, view, services) =>
        {
            // The layout loads the circuit's scopes before a page renders; the in-process render has no layout.
            await services.GetRequiredService<SerginScopeSession>().InitializeAsync();
            current = services.GetRequiredService<IAuthorizationScopeContext>().ScopeId;
            IDialogReference opened = await ((SerginGeneratedListView<TaskViewProbe>)view).ShowSchedulesAsync(RefreshName);

            // The dialog's content does not render in process: its parameters and title are read off the instance.
            SerginTaskSchedulesDialog dialog = Assert.IsType<SerginTaskSchedulesDialog>(opened.Dialog);
            Assert.Equal((TasksDbContext.Schema, RefreshName, current), (dialog.Schema, dialog.TaskName, dialog.ScopeId));
            Assert.Equal($"Refresh probes · {scopeName}", dialog.Heading);
        });

        // What the dialog holds: the section with no record, listing the collection task's schedules in the scope only.
        await RenderProbeAsync<SerginTaskSchedulesSection>(
            "/tst/task-view-probes",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(SerginTaskSchedulesSection.Schema)] = TasksDbContext.Schema,
                [nameof(SerginTaskSchedulesSection.TargetId)] = null,
                [nameof(SerginTaskSchedulesSection.TaskName)] = RefreshName,
                [nameof(SerginTaskSchedulesSection.ScopeId)] = current,
            },
            (root, _, _) =>
            {
                string html = WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("17 3 * * *", html, StringComparison.Ordinal);
                Assert.Contains("Refresh probes", html, StringComparison.Ordinal);
                Assert.DoesNotContain("29 4 * * *", html, StringComparison.Ordinal);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task CollectionTask_ScheduleRequest_HasNoTargets_AndNamesTheScope()
    {
        await GrantAsync(TaskViewProbePermissions.Run, TaskPermissions.Manage);

        await RenderProbeAsync<SerginGeneratedListView<TaskViewProbe>>("/tst/task-view-probes", [], (_, view, _) =>
        {
            SerginScheduleRequest request =
                ((SerginGeneratedListView<TaskViewProbe>)view).ScheduleRequestFor(CommandOf(typeof(RefreshTaskViewProbesCommand)));

            Assert.Empty(request.Targets);
            Assert.Single(request.TargetLabels);
            Assert.Equal(TaskUiTexts.Dialog.Scope, request.TargetsCaption);
            Assert.IsType<RefreshTaskViewProbesCommand>(request.BuildTemplate());
            return Task.CompletedTask;
        });
    }

    // ---- Helpers ----

    private static Dictionary<string, object?> Id(Guid id) => new(StringComparer.Ordinal) { ["Id"] = id };

    private static SerginPageAction ActionOf(IReadOnlyList<SerginPageAction> actions, TextDefinition label) =>
        Assert.Single(actions, action => action.Label == label);

    private CommandView CommandOf(Type request) =>
        Assert.Single(host.Services.GetRequiredService<AggregateViewRegistry>().For<TaskViewProbe>().Commands, command => command.Request == request);

    private CommandView ReadProfile() =>
        Assert.Single(
            factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>().Commands,
            command => command.Request == typeof(ReadDeviceProfileCommand));

    private static ActivityRow EntityRow(string commandName, Guid target, RunOutcome state) =>
        new(new TaskRunRef("dm", Guid.CreateVersion7()), null, commandName, "DEV", null, state, DateTime.UtcNow, null, 0, 1, null, null, target, null, null, 0);

    private static ActivityRow CollectionRow(string commandName, Guid scope, RunOutcome state) =>
        new(new TaskRunRef("tst", Guid.CreateVersion7()), null, commandName, "Scope", null, state, DateTime.UtcNow, null, 0, 1, null, null, null, null, null, 0, ScopeId: scope);

    private async Task GrantAsync(params PermissionDefinition[] permissions)
    {
        users.Current = null;
        users.Current = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, [.. permissions.Select(permission => permission.Permission)]);
    }

    private async Task<string> RootScopeNameAsync()
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        IReadOnlyList<ScopeOption> scopes = await scope.ServiceProvider.GetRequiredService<IScopeDirectory>().GetReachableAsync();

        return Assert.Single(scopes, option => option.Id == Scope.RootId.Value).Name;
    }

    private async Task CreateScheduleAsync(ITaskCommand template, IReadOnlyCollection<TaskScheduleTarget> targets, string cron)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ErrorOr<CreateTaskScheduleCommandResponse> created = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CreateTaskScheduleCommand(TasksDbContext.Schema, template, targets, cron, "UTC"));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
    }

    private async Task RenderProbeAsync<TView>(
        string path, Dictionary<string, object?> parameters, Func<HtmlRootComponent, IComponent, IServiceProvider, Task> test)
        where TView : IComponent
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;

        await GeneratedViewRendering.RenderAsync<TView>(scope, path, parameters, (root, view) => test(root, view, scope.ServiceProvider));
    }

    internal static async Task<IReadOnlyList<Guid>> DevicesAsync(ISerginDispatcher dispatcher, int count)
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

    private async Task<IReadOnlyList<string?>> LabelsAsync(TaskRunRef run)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT target_label FROM dm.task_run_items WHERE run_id = @id";
        AddId(command, run.RunId);
        List<string?> labels = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            labels.Add(await reader.IsDBNullAsync(0) ? null : reader.GetString(0));
        }

        return labels;
    }

    internal static async Task<object?> ScalarAsync(WebApplicationFactory<Program> app, string sql, Guid? id = null)
    {
        using IServiceScope scope = app.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
#pragma warning disable CA2100, S2077 // test-only SQL with constant identifiers; the one value is a parameter
        command.CommandText = sql;
#pragma warning restore CA2100, S2077

        if (id is { } value)
        {
            AddId(command, value);
        }

        return await command.ExecuteScalarAsync();
    }

    private static void AddId(DbCommand command, Guid id)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
    }
}

/// <summary>Renders a generated view in process, with the providers its menus and dialogs render into, and hands the test the view.</summary>
internal static class GeneratedViewRendering
{
    public static async Task RenderAsync<TView>(
        AsyncServiceScope scope, string path, Dictionary<string, object?> parameters, Func<HtmlRootComponent, IComponent, Task> test)
        where TView : IComponent
    {
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost{path}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            IComponent? view = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<ViewHost<TView>>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(ViewHost<TView>.ViewParameters)] = parameters,
                    [nameof(ViewHost<TView>.Capture)] = (Action<IComponent>)(instance => view = instance),
                }));

            await test(root, view!);
        });
    }

    /// <summary>A generated view with the providers its menus and dialogs render into; hands the test the view.</summary>
    private sealed class ViewHost<TView> : ComponentBase
        where TView : IComponent
    {
        [Parameter]
        public Dictionary<string, object?> ViewParameters { get; set; } = default!;

        [Parameter]
        public Action<IComponent> Capture { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<TView>(2);

            foreach ((string name, object? value) in ViewParameters)
            {
                builder.AddComponentParameter(3, name, value);
            }

            builder.AddComponentReferenceCapture(4, instance => Capture((IComponent)instance));
            builder.CloseComponent();
        }
    }
}
