using System.Net;
using System.Text.RegularExpressions;
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
using Sergin.MeterMinder.IntegrationTests.All.Aggregates;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskArgumentProbes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Scopes;
using Sergin.SharedKernel.Presentation.Blazor.Tasks;
using Sergin.SharedKernel.Presentation.Blazor.TimeZones;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The Schedule dialog of a generated view's task asks for the task's arguments above the trigger, validates them as a
/// start does (field by field, by the task's own validator; Save refused while one is invalid), and stores what was
/// entered, which a cron fire then runs with. Its title names the record count, or a collection task's scope. Edit
/// reopens the dialog with the saved arguments editable, for a task over records and for a task over the collection.
/// </summary>
/// <remarks>
/// The dialog renders in process inside <see cref="ProbeDialogProviders"/>, as <see cref="TaskUiTests"/> renders it; the
/// tests set an argument as its input would (<c>SetArgument</c>) and save as the Save button does.
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class ScheduleDialogArgumentsTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ShiftName = "tst.probes.shift";
    private const string RefreshName = "tst.probes.refresh";

    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;

    // The renderer RenderSectionAsync opened, so a dialog the test opens inside it renders beside the section.
    private HtmlRootComponent currentRoot;
    private IServiceProvider? currentServices;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                ProbeHandlers.AddTo(services);
                ScheduleProbeUiModule.AddTo(services);
                ProbeUserContextFactory.AddTo(services);
                ScheduleProbeViewHost.AddTo(services, typeof(DialogScheduleProbeViewConfiguration));
                services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
            }));

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        users.Current = await tree.CreateGrantedCallerAsync(
            Scope.RootId.Value,
            TaskPermissions.Manage.Permission,
            ScheduleProbePermissions.Read.Permission,
            ScheduleProbePermissions.Touch.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ScheduleDialog_ShowsArgumentsAboveCron_AndStoresThem()
    {
        Guid[] probes = [await CreateProbeAsync("Probe A"), await CreateProbeAsync("Probe B"), await CreateProbeAsync("Probe C")];
        CommandView shift = CommandOf(typeof(ProbeShiftTaskCommand));
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ScheduleProbe>();

        SerginScheduleRequest request = GeneratedListBody<ScheduleProbeListItem>.ScheduleRequestFor(
            model, shift, [.. probes.Select(probe => new ScheduleProbeListItem(probe, "Probe"))]);

        Assert.Same(shift, request.TaskView);
        Assert.NotNull(request.ArgumentValues);

        await RenderDialogAsync(request, async (dialog, html) =>
        {
            string opened = html();
            int note = opened.IndexOf(">Note<", StringComparison.Ordinal);
            int hours = opened.IndexOf(">Hours<", StringComparison.Ordinal);
            int trigger = opened.IndexOf(">Schedule</", StringComparison.Ordinal);
            Assert.True(note >= 0 && hours > note && trigger > hours, "The arguments are not shown above the trigger.");

            // A value the task's validator refuses: the field says so, Save is disabled and nothing is stored.
            dialog.SetArgument(nameof(ProbeShiftTaskCommand.Note), "night");
            dialog.SetArgument(nameof(ProbeShiftTaskCommand.Hours), 0m);
            await dialog.SaveAsync();

            Assert.Contains("must be between 1 and 24", html(), StringComparison.Ordinal);
            Assert.Matches(SaveDisabled(), html());
            Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM tst.task_schedules WHERE command_name = '{ShiftName}'"));

            dialog.SetArgument(nameof(ProbeShiftTaskCommand.Hours), 4m);
            await dialog.SaveAsync();
        });

        string arguments = (string)(await GeneratedTaskViewTests.ScalarAsync(
            host, $"SELECT arguments::text FROM tst.task_schedules WHERE command_name = '{ShiftName}'"))!;
        Assert.Contains("night", arguments, StringComparison.Ordinal);
        Assert.Matches(Hours(4), arguments);

        // A cron fire runs every record with the stored arguments.
        var scheduleId = (Guid)(await GeneratedTaskViewTests.ScalarAsync(host, $"SELECT id FROM tst.task_schedules WHERE command_name = '{ShiftName}'"))!;

        using (IServiceScope scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskScheduleRunner>()
                .FireAsync(TasksDbContext.Schema, new TaskScheduleId(scheduleId), CancellationToken.None);
        }

        await host.Services.GetRequiredService<FakeTaskEngine>().DrainAsync(host.Services);

        IReadOnlyCollection<ProbeShiftTaskCommand> sent = host.Services.GetRequiredService<ShiftRecorder>().Sent;
        Assert.Equal(probes.Order(), sent.Select(command => command.Id.Value).Order());
        Assert.All(sent, command => Assert.Equal((new ProbeNote("night"), 4), (command.Note, command.Hours)));
    }

    [Fact]
    public async Task ScheduleDialog_Header_NamesRecordsOrScope()
    {
        Guid one = await CreateProbeAsync("Probe one");
        Guid two = await CreateProbeAsync("Probe two");
        Guid three = await CreateProbeAsync("Probe three");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ScheduleProbe>();
        CommandView shift = CommandOf(typeof(ProbeShiftTaskCommand));

        SerginScheduleRequest overThree = GeneratedListBody<ScheduleProbeListItem>.ScheduleRequestFor(
            model, shift, [new ScheduleProbeListItem(one, "a"), new ScheduleProbeListItem(two, "b"), new ScheduleProbeListItem(three, "c")]);
        await RenderDialogAsync(overThree, (_, html) =>
        {
            Assert.Contains("Shift probe · 3 records", html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });

        SerginScheduleRequest overOne = GeneratedListBody<ScheduleProbeListItem>.ScheduleRequestFor(model, shift, [new ScheduleProbeListItem(one, "a")]);
        await RenderDialogAsync(overOne, (_, html) =>
        {
            Assert.Contains("Shift probe · 1 record", html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });

        // A collection task names the scope it runs in, as its list view's Schedule… builds the request.
        CommandView refresh = CommandOf(typeof(ProbeRefreshTaskCommand));
        GeneratedFormValues values = SerginScheduleArguments.Defaults(refresh);
        SerginScheduleRequest inScope = new(
            TasksDbContext.Schema,
            refresh.Label,
            () => GeneratedViewSupport.CollectionTemplateOf(refresh, values.Values),
            [],
            ["North Region"],
            TargetsCaption: TaskUiTexts.Dialog.Scope,
            TaskView: refresh,
            ArgumentValues: values);
        await RenderDialogAsync(inScope, (_, html) =>
        {
            Assert.Contains("Refresh probes · North Region", html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EditSchedule_ChangesArguments()
    {
        Guid probe = await CreateProbeAsync("Probe edited");
        TaskScheduleId id = await CreateScheduleAsync(
            new ProbeShiftTaskCommand(new ScheduleProbeId(probe), new ProbeNote("day"), 4), [new TaskScheduleTarget(probe)]);
        Guid before = await RowVersionAsync(id);

        await RenderSectionAsync(probe, taskName: null, async section =>
        {
            SerginScheduleRequest request = (await section.EditRequestAsync(id))!;

            Assert.Equal(ShiftName, request.TaskView!.Task!.Name);

            await OpenAsync(request, async (dialog, html) =>
            {
                Assert.Contains("Edit schedule · 1 record", html(), StringComparison.Ordinal);
                Assert.Matches(InputValue("4"), html());
                Assert.Matches(InputValue("day"), html());

                dialog.SetArgument(nameof(ProbeShiftTaskCommand.Hours), 8m);
                await dialog.SaveAsync();
            });
        });

        string arguments = (string)(await GeneratedTaskViewTests.ScalarAsync(host, "SELECT arguments::text FROM tst.task_schedules WHERE id = @id", id.Value))!;
        Assert.Matches(Hours(8), arguments);
        Assert.Contains("day", arguments, StringComparison.Ordinal);
        Assert.NotEqual(before, await RowVersionAsync(id));
    }

    [Fact]
    public async Task EditCollectionSchedule_NamesTheScope_AndChangesArguments()
    {
        TaskScheduleId id = await CreateScheduleAsync(new ProbeRefreshTaskCommand(new ProbeNote("first")), []);
        Guid before = await RowVersionAsync(id);

        await RenderSectionAsync(targetId: null, RefreshName, async section =>
        {
            SerginScheduleRequest request = (await section.EditRequestAsync(id))!;
            string scopeName = Assert.Single(request.TargetLabels);

            Assert.Empty(request.Targets);
            Assert.Equal(TaskUiTexts.Dialog.Scope, request.TargetsCaption);
            Assert.IsType<ProbeRefreshTaskCommand>(request.BuildTemplate());

            await OpenAsync(request, async (dialog, html) =>
            {
                Assert.Contains($"Edit schedule · {scopeName}", html(), StringComparison.Ordinal);
                Assert.Matches(InputValue("first"), html());

                dialog.SetArgument(nameof(ProbeRefreshTaskCommand.Note), "second");
                await dialog.SaveAsync();
            });
        });

        string arguments = (string)(await GeneratedTaskViewTests.ScalarAsync(host, "SELECT arguments::text FROM tst.task_schedules WHERE id = @id", id.Value))!;
        Assert.Contains("second", arguments, StringComparison.Ordinal);
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM tst.task_schedule_targets WHERE schedule_id = '{id.Value}'"));
        Assert.NotEqual(before, await RowVersionAsync(id));
    }

    // ---- Helpers ----

    [GeneratedRegex(@"<button[^>]*disabled[^>]*>\s*(<[^>]*>\s*)*Save")]
    private static partial Regex SaveDisabled();

    private static Regex Hours(int value) =>
        new($"\"hours\"\\s*:\\s*{value}\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static Regex InputValue(string value) =>
        new($"<input[^>]*value=\"{Regex.Escape(value)}\"", RegexOptions.None, TimeSpan.FromSeconds(1));

    private CommandView CommandOf(Type request) =>
        Assert.Single(host.Services.GetRequiredService<AggregateViewRegistry>().For<ScheduleProbe>().Commands, command => command.Request == request);

    private async Task<long> CountAsync(string sql) => (long)(await GeneratedTaskViewTests.ScalarAsync(host, sql))!;

    private async Task<Guid> RowVersionAsync(TaskScheduleId id) =>
        (Guid)(await GeneratedTaskViewTests.ScalarAsync(host, "SELECT row_version FROM tst.task_schedules WHERE id = @id", id.Value))!;

    private async Task<Guid> CreateProbeAsync(string name)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var probe = ScheduleProbe.Create();
        probe.Touch(new ProbeNote(name));
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe.Id.Value;
    }

    private async Task<TaskScheduleId> CreateScheduleAsync(ITaskCommand template, IReadOnlyCollection<TaskScheduleTarget> targets)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);
        ErrorOr<CreateTaskScheduleCommandResponse> created = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CreateTaskScheduleCommand(TasksDbContext.Schema, template, targets, "0 2 * * *", "UTC"));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return new TaskScheduleId(created.Value.Id);
    }

    private AsyncServiceScope OpenScope()
    {
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", "http://localhost/tst/probes");

        return scope;
    }

    // Renders the dialog providers, opens the dialog on the request, and hands the test the dialog once it rendered.
    private async Task RenderDialogAsync(SerginScheduleRequest request, Func<SerginScheduleDialog, Func<string>, Task> test)
    {
        await using AsyncServiceScope scope = OpenScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<ProbeDialogProviders>();
            await OpenOnAsync(scope.ServiceProvider, root, request, test);
        });
    }

    // Renders the schedules section (of a record, or of a collection task in the root scope) beside the dialog providers.
    private async Task RenderSectionAsync(Guid? targetId, string? taskName, Func<SerginTaskSchedulesSection, Task> test)
    {
        await using AsyncServiceScope scope = OpenScope();

        // The layout loads the circuit's scopes before a page renders; the in-process render has no layout.
        await scope.ServiceProvider.GetRequiredService<SerginScopeSession>().InitializeAsync();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SectionWithDialogs.Section = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<SectionWithDialogs>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(SectionWithDialogs.TargetId)] = targetId,
                    [nameof(SectionWithDialogs.TaskName)] = taskName,
                    [nameof(SectionWithDialogs.ScopeId)] = taskName is null ? null : Scope.RootId.Value,
                }));

            currentRoot = root;
            currentServices = scope.ServiceProvider;
            await test(SectionWithDialogs.Section!);
        });
    }

    // Inside RenderSectionAsync: opens the dialog on the section's renderer.
    private Task OpenAsync(SerginScheduleRequest request, Func<SerginScheduleDialog, Func<string>, Task> test) =>
        OpenOnAsync(currentServices!, currentRoot, request, test);

    private static async Task OpenOnAsync(
        IServiceProvider services, HtmlRootComponent root, SerginScheduleRequest request, Func<SerginScheduleDialog, Func<string>, Task> test)
    {
        IDialogReference reference = await services.GetRequiredService<IDialogService>().OpenScheduleAsync(request);
        string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

        // The dialog renders after the provider is told, and an edit only once it has read the saved schedule.
        for (int attempt = 0; attempt < 50 && !(reference.Dialog is SerginScheduleDialog { } shown
                 && (request.EditingId is null || shown.Version is not null) && Html().Contains("Run now", StringComparison.Ordinal)); attempt++)
        {
            await Task.Delay(100);
        }

        await test(Assert.IsType<SerginScheduleDialog>(reference.Dialog), Html);
    }

    /// <summary>The schedules section beside the providers its dialogs render into.</summary>
    private sealed class SectionWithDialogs : ComponentBase
    {
        public static SerginTaskSchedulesSection? Section { get; set; }

        [Parameter]
        public Guid? TargetId { get; set; }

        [Parameter]
        public string? TaskName { get; set; }

        [Parameter]
        public Guid? ScopeId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<SerginTaskSchedulesSection>(2);
            builder.AddComponentParameter(3, nameof(SerginTaskSchedulesSection.Schema), TasksDbContext.Schema);
            builder.AddComponentParameter(4, nameof(SerginTaskSchedulesSection.TargetId), TargetId);
            builder.AddComponentParameter(5, nameof(SerginTaskSchedulesSection.TaskName), TaskName);
            builder.AddComponentParameter(6, nameof(SerginTaskSchedulesSection.ScopeId), ScopeId);
            builder.AddComponentParameter(7, nameof(SerginTaskSchedulesSection.PollInterval), TimeSpan.FromHours(1));
            builder.AddComponentReferenceCapture(8, instance => Section = (SerginTaskSchedulesSection)instance);
            builder.CloseComponent();
        }
    }
}
