using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Shell;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Queries;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Lists;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Navigation;
using Sergin.SharedKernel.Presentation.Blazor.Tasks;
using Sergin.SharedKernel.Presentation.Blazor.Tasks.Runs;
using Sergin.SharedKernel.Presentation.Blazor.TimeZones;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The Task runs page: prerendered through the real host (DeviceManagement is its only module running tasks), and its
/// view rendered in process and loaded by hand, since the table loads after the first interactive render. The pure
/// parts — the module order and the trigger line — are tested without a renderer.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class TaskRunsPageTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Dm = "dm";
    private const string ReadProfile = "dm.device.read-device-profile";
    private const string Cron = "0 9 * * *";
    private const string ModuleSelector = "id=\"sergin-task-runs-module\"";

    private readonly List<Guid> seededRuns = [];
    private readonly List<TaskScheduleId> seededSchedules = [];

    public Task InitializeAsync() => Task.CompletedTask;

    // The shared host outlives this class: what it seeded in dm is removed, so no other test sees these runs.
    public async Task DisposeAsync()
    {
        foreach (Guid run in seededRuns)
        {
            await ExecuteAsync("DELETE FROM dm.task_runs WHERE id = @id", run);
        }

        foreach (TaskScheduleId schedule in seededSchedules)
        {
            await ExecuteAsync("DELETE FROM dm.task_schedules WHERE id = @id", schedule.Value);
        }
    }

    [Fact]
    public void Modules_FollowTheUiModulesOrder_ThenOrdinal()
    {
        ModuleConfigurationSource[] sources =
        [
            ModuleConfigurationSource.FromTypesInSchema("zz", typeof(ScheduleProbeAggregateFeatureConfiguration), typeof(UnconfiguredProbeCommand)),
            ModuleConfigurationSource.FromTypesInSchema("bb", typeof(SyncProbesCommandConfiguration)),
            ModuleConfigurationSource.FromTypesInSchema("tst", typeof(TouchProbeCommandConfiguration), typeof(TouchOneProbeCommandConfiguration)),
        ];
        var tasks = TaskCatalog.FromSources(sources, CommandConfigurationRegistry.FromSources(sources));

        // "aa" runs no task; "tst" comes before "zz" by the drawer, not by name; "bb" has no UI module, so it comes last.
        var ui = new SerginUiModuleCatalog([new UiModule("aa"), new UiModule("zz"), new UiModule("tst")]);

        Assert.Equal(["zz", "tst", "bb"], TaskRunRows.Modules(tasks, ui));
    }

    [Fact]
    public void StartedByLine_PerTrigger()
    {
        Assert.Equal((TaskUiTexts.RunList.TriggerManual, null), TaskRunRows.StartedByLine(Run(TaskRunTrigger.Manual, cron: null)));
        Assert.Equal((TaskUiTexts.RunList.TriggerOnce, null), TaskRunRows.StartedByLine(Run(TaskRunTrigger.Once, cron: null)));
        Assert.Equal((TaskUiTexts.RunList.TriggerScheduleNow, Cron), TaskRunRows.StartedByLine(Run(TaskRunTrigger.ScheduleNow, Cron)));
        Assert.Equal((TaskUiTexts.RunList.TriggerSchedule, Cron), TaskRunRows.StartedByLine(Run(TaskRunTrigger.Schedule, Cron)));

        // A cron run whose schedule was deleted: the join finds no live schedule.
        Assert.Equal((TaskUiTexts.RunList.TriggerSchedule, null), TaskRunRows.StartedByLine(Run(TaskRunTrigger.Schedule, cron: null)));
    }

    /// <summary>
    /// The page's trail starts at its own route, so the breadcrumb names the drawer group of the nav entry linking there.
    /// The entry is the shell's (added with the host wiring); the host here carries it, as the wired host will.
    /// </summary>
    [Fact]
    public async Task Page_Prerenders_UnderTheAdministrationTrail()
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            ServiceDescriptor registered = services.Last(descriptor => descriptor.ServiceType == typeof(SerginUiModuleCatalog));
            var catalog = (SerginUiModuleCatalog)registered.ImplementationInstance!;
            SerginNavItem runs = new(TaskUiTexts.RunList.Heading, SerginTaskRunsPage.Route, Icons.Material.Filled.WorkHistory, 400, TaskPermissions.Manage);

            services.Remove(registered);
            services.AddSingleton(new SerginUiModuleCatalog(catalog.Modules, [new SerginShellNavEntry(SerginNavGroups.Administration, null, [runs])]));
        }));

        string html = await GetAsync(host, SerginTaskRunsPage.Route);
        string strip = BreadcrumbNav().Match(html).Value;

        Assert.Contains("<title>Task runs</title>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Administration\"", strip, StringComparison.Ordinal);
        Assert.Contains("Task runs", strip, StringComparison.Ordinal);
        Assert.True(
            strip.IndexOf("Administration", StringComparison.Ordinal) < strip.IndexOf("Task runs", StringComparison.Ordinal),
            $"expected Administration > Task runs in {strip}");
    }

    [Fact]
    public async Task ModuleSelector_HiddenWithOneModule()
    {
        string html = await GetAsync(factory, SerginTaskRunsPage.Route);

        Assert.Contains("sergin-aggregate-list", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ModuleSelector, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>?module=zz</c> names no module that runs tasks: the page prerenders the list, and the view loads the first
    /// module's runs (dm, the only one here) rather than sending zz.
    /// </summary>
    [Fact]
    public async Task UnknownModuleParameter_FallsBackToTheDefault()
    {
        string fallback = await GetAsync(factory, $"{SerginTaskRunsPage.Route}?module=zz");

        Assert.Contains("sergin-aggregate-list", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("mud-alert", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("No module runs tasks in this application.", fallback, StringComparison.Ordinal);

        SendLog sent = new();

        await RenderViewAsync(catalog: null, sent, async (view, _) =>
        {
            await view.ReloadAsync();
            await sent.WaitForAsync<GetTaskRunsQuery>();
        }, module: "zz");

        (GetTaskRunsQuery query, bool failed) = Assert.Single(sent.Of<GetTaskRunsQuery>());
        Assert.Equal(Dm, query.Schema);
        Assert.False(failed);
    }

    /// <summary>
    /// Every field of the filter panel, set, makes the filters Apply sends; the run list query takes them (the date range
    /// as one between filter of whole days) and still finds the run they describe.
    /// </summary>
    [Fact]
    public async Task AppliedFilters_PassTheRunListQuery()
    {
        UserId person = DevUserId();
        DateTime startedAt = DateTime.UtcNow;
        Guid run = await AddRunAsync(new RunSeed(TaskRunTrigger.Manual, startedAt) { Labels = ["MTR-0201"] });
        IReadOnlyList<SerginAppliedFilter> filters = [];

        await RenderViewAsync(catalog: null, log: null, (view, _) =>
        {
            view.TaskFilter = ReadProfile;
            view.StatusFilter = RunOutcome.Running;
            view.StartedByFilter = person.Value;
            view.TriggerFilter = TaskRunTrigger.Manual;
            view.StartedFilter = new DateRange(startedAt.Date, startedAt.Date);
            filters = view.BuildFilters();

            return Task.CompletedTask;
        });

        Assert.Equal(
            [TaskRunListFields.Task, TaskRunListFields.Status, TaskRunListFields.StartedBy, TaskRunListFields.Trigger, TaskRunListFields.StartedAt],
            filters.Select(filter => filter.Id));

        using IServiceScope scope = factory.Services.CreateScope();
        ErrorOr.ErrorOr<ListQueryResponse<TaskRunsListItem>> result = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new GetTaskRunsQuery(Dm, Paggination.Create(50, 1), filtering: SerginListQueryParts.ToFiltering(filters)));

        Assert.False(result.IsError, result.IsError ? $"{result.FirstError.Code}: {result.FirstError.Description}" : string.Empty);
        Assert.Contains(run, result.Value.Data.Select(item => item.Id));
        Assert.All(result.Value.Data, item => Assert.Equal(RunOutcome.Running, item.Outcome));
    }

    [Fact]
    public async Task NoOptedInModule_ShowsTheEmptyStateAndSendsNothing()
    {
        Assert.Empty(TaskRunRows.Modules(TaskCatalog.Empty, factory.Services.GetRequiredService<SerginUiModuleCatalog>()));

        SendLog sent = new();

        await RenderViewAsync(
            TaskCatalog.Empty,
            sent,
            async (view, html) =>
            {
                await view.ReloadAsync();
                view.StartPolling();

                Assert.Contains("No module runs tasks in this application.", html(), StringComparison.Ordinal);
                Assert.DoesNotContain("sergin-aggregate-list", html(), StringComparison.Ordinal);
                Assert.False(view.IsPolling);
            });

        Assert.Equal(0, sent.Count);
    }

    [Fact]
    public async Task Rows_RenderTheirCells()
    {
        UserId person = DevUserId();
        DateTime now = DateTime.UtcNow;
        var device = Guid.CreateVersion7();
        TaskScheduleId schedule = await AddScheduleAsync(person, targets: 12);

        Guid manual = await AddRunAsync(new RunSeed(TaskRunTrigger.Manual, now.AddMinutes(-3)) { Target = device, Labels = ["MTR-0042"], Succeeded = 1 });
        Guid scheduled = await AddRunAsync(new RunSeed(TaskRunTrigger.Schedule, now.AddMinutes(-2)) { Schedule = schedule, Items = 12, Succeeded = 11, Failed = 1 });
        Guid cancelled = await AddRunAsync(new RunSeed(TaskRunTrigger.Once, now.AddSeconds(-150)) { Items = 5, Succeeded = 2, Cancelled = 3 });
        Guid failed = await AddRunAsync(new RunSeed(TaskRunTrigger.Once, now.AddSeconds(-100)) { Labels = ["MTR-0044"], Failed = 1 });
        Guid running = await AddRunAsync(new RunSeed(TaskRunTrigger.Manual, now.AddMinutes(-1)) { Labels = ["MTR-0043"] });
        string name = await DisplayNameAsync(person);

        await RenderViewAsync(catalog: null, log: null, async (view, html) =>
        {
            await LoadAsync(view, html, "MTR-0042");

            string manualRow = RowOf(html(), manual);
            Assert.Contains(">Read profile</", manualRow, StringComparison.Ordinal);
            Assert.Contains($"href=\"/dm/devices/{device}\"", manualRow, StringComparison.Ordinal);
            Assert.Contains("MTR-0042", manualRow, StringComparison.Ordinal);
            Assert.Contains("Succeeded", manualRow, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-label=\"Cancel\"", manualRow, StringComparison.Ordinal);

            string scheduledRow = RowOf(html(), scheduled);
            Assert.Contains("12 records", scheduledRow, StringComparison.Ordinal);
            Assert.Matches($@"{Regex.Escape(name)}</p>\s*<span[^>]*>Schedule · {Regex.Escape(Cron)}</span>", scheduledRow);
            Assert.Contains("Partly failed", scheduledRow, StringComparison.Ordinal);
            Assert.Contains("11 of 12 succeeded, 1 failed", scheduledRow, StringComparison.Ordinal);
            Assert.DoesNotContain("Completed", scheduledRow, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-label=\"Cancel\"", scheduledRow, StringComparison.Ordinal);

            // The counts under the outcome carry no status word of their own, so they cannot contradict it.
            string cancelledRow = RowOf(html(), cancelled);
            Assert.Matches(@">Cancelled</p>\s*<span[^>]*>2 of 5 succeeded, 0 failed, 3 cancelled</span>", cancelledRow);
            Assert.DoesNotContain("Completed", cancelledRow, StringComparison.Ordinal);

            string failedRow = RowOf(html(), failed);
            Assert.Matches(@">Failed</p>\s*<span[^>]*>0 of 1 succeeded, 1 failed</span>", failedRow);
            Assert.DoesNotContain("Completed", failedRow, StringComparison.Ordinal);

            string runningRow = RowOf(html(), running);
            Assert.Contains("Running", runningRow, StringComparison.Ordinal);
            Assert.Contains("mud-progress-linear", runningRow, StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Cancel\"", runningRow, StringComparison.Ordinal);
            Assert.True(view.HasRunning);

            // The poll runs from the first render and ends with the view.
            view.StartPolling();
            Assert.True(view.IsPolling);
            await view.DisposeAsync();
            Assert.False(view.IsPolling);
        });
    }

    [Fact]
    public async Task TaskLink_OpensTheRecordsDialog_WithTheStoredLabels()
    {
        UserId person = DevUserId();
        Guid run = await AddRunAsync(new RunSeed(TaskRunTrigger.Manual, DateTime.UtcNow) { Items = 3, Labels = ["MTR-0101", "MTR-0102", "MTR-0103"], Succeeded = 2, Failed = 1 });
        string name = await DisplayNameAsync(person);
        TaskRunsListItem listed = await ListedAsync(run);

        await RenderViewAsync(catalog: null, log: null, async (view, html) =>
        {
            await view.OpenRecordsAsync(listed);

            for (int attempt = 0; attempt < 50 && !html().Contains("sergin-run-items", StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(100);
            }

            string dialog = html()[html().IndexOf("mud-dialog-container", StringComparison.Ordinal)..];
            Assert.Contains("Read profile", dialog, StringComparison.Ordinal);
            Assert.Contains("Started", dialog, StringComparison.Ordinal);
            Assert.Contains(name, dialog, StringComparison.Ordinal);

            string items = dialog[dialog.IndexOf("sergin-run-items", StringComparison.Ordinal)..];
            Assert.Matches(@"<td>MTR-0101</td>\s*<td>Succeeded</td>", items);
            Assert.Matches(@"<td>MTR-0102</td>\s*<td>Succeeded</td>", items);
            Assert.Matches(@"<td>MTR-0103</td>\s*<td>Failed</td>", items);
            Assert.Contains("Finished", items, StringComparison.Ordinal);
        });
    }

    [GeneratedRegex("<nav aria-label=\"Breadcrumb\">.*?</nav>", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BreadcrumbNav();

    private static TaskRunsListItem Run(TaskRunTrigger trigger, string? cron) =>
        new(Guid.CreateVersion7(), ReadProfile, trigger, Guid.CreateVersion7(), cron is null ? null : Guid.CreateVersion7(), cron, cron is null ? null : "UTC",
            RunOutcome.Succeeded, DateTime.UtcNow, DateTime.UtcNow, 1, 1, 0, 0, 0, null, null, null, true, null);

    // The table row of one run, found by the dialog link's row: every row carries its task link, so rows are split on <tr.
    private static string RowOf(string html, Guid run)
    {
        string marker = $"data-run=\"{run}\"";
        string row = html.Split("<tr", StringSplitOptions.None).SingleOrDefault(part => part.Contains(marker, StringComparison.Ordinal)) ?? string.Empty;

        Assert.False(row.Length == 0, $"no row for run {run}");

        return row;
    }

    private static async Task LoadAsync(SerginTaskRunsView view, Func<string> html, string expected)
    {
        await view.ReloadAsync();

        for (int attempt = 0; attempt < 50 && !html().Contains(expected, StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100);
        }
    }

    private UserId DevUserId()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IUserContext>().Id;
    }

    private async Task<string> DisplayNameAsync(UserId user)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IReadOnlyDictionary<Guid, string> names =
            await scope.ServiceProvider.GetRequiredService<IUserDirectory>().GetDisplayNamesAsync([user.Value]);

        return names[user.Value];
    }

    private async Task<TaskRunsListItem> ListedAsync(Guid run)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ErrorOr.ErrorOr<ListQueryResponse<TaskRunsListItem>> result = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new GetTaskRunsQuery(Dm, Paggination.Create(50, 1)));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);

        return result.Value.Data.Single(item => item.Id == run);
    }

    private async Task<TaskScheduleId> AddScheduleAsync(UserId savedBy, int targets)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);

        var schedule = TaskSchedule.Create(
            ReadProfile, "{}", [.. Enumerable.Range(0, targets).Select(_ => new TaskScheduleTarget(Guid.CreateVersion7()))], Cron, "UTC", savedBy);
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Dm);
        store.AddTask(schedule);
        await store.SaveAsync(CancellationToken.None);

        seededSchedules.Add(schedule.Id);

        return schedule.Id;
    }

    /// <summary>Saves one dm run at the root, acting as the development user (the viewer), settled as the seed says.</summary>
    private async Task<Guid> AddRunAsync(RunSeed seed)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(Scope.RootId.Value))!;
        TaskRunDefinition definition = new(seed.Schedule, seed.Trigger, DevUserId(), Scope.RootId.Value, path, ReadProfile, "{}");
        DateTime at = DateTime.UtcNow;

        var run = TaskRun.Start(definition, seed.Items, seed.StartedAtUtc);
        IReadOnlyList<TaskRunItem> items =
        [
            .. Enumerable.Range(0, seed.Items).Select(index => TaskRunItem.Create(
                run.Id,
                index == 0 && seed.Target is { } target ? target : Guid.CreateVersion7(),
                at,
                index < seed.Labels.Count ? seed.Labels[index] : null)),
        ];

        if (seed.Succeeded + seed.Failed + seed.Cancelled > 0)
        {
            for (int index = 0; index < seed.Items; index++)
            {
                if (index < seed.Succeeded)
                {
                    items[index].MarkSucceeded(at);
                }
                else if (index < seed.Succeeded + seed.Failed)
                {
                    items[index].MarkFailed("Probe.Failed", "The probe failed.", at);
                }
                else
                {
                    items[index].MarkCancelled(at);
                }
            }

            run.Complete(seed.Succeeded, seed.Failed, seed.Cancelled, at);
        }

        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Dm);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        seededRuns.Add(run.Id);

        return run.Id;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
#pragma warning disable CA2100, S2077 // test-only SQL with constant identifiers; the one value is a parameter
        command.CommandText = sql;
#pragma warning restore CA2100, S2077
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Renders the view in process as the development user, with the providers its menus and dialogs render into; the
    /// browser's zone is UTC (no interop here), the task catalogue the given one when set, the page's <c>?module=</c>
    /// value passed on, and every send recorded in <paramref name="log"/> when given.
    /// </summary>
    private async Task RenderViewAsync(
        TaskCatalog? catalog, SendLog? log, Func<SerginTaskRunsView, Func<string>, Task> act, string? module = null)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost{SerginTaskRunsPage.Route}");

        Dictionary<Type, object> overrides = new()
        {
            [typeof(IUiTimeZoneStore)] = new FixedTimeZoneStore(),
            [typeof(ISerginDispatcher)] = new RecordingDispatcher(scope.ServiceProvider.GetRequiredService<ISerginDispatcher>(), log ?? new SendLog()),
        };

        if (catalog is not null)
        {
            overrides[typeof(TaskCatalog)] = catalog;
        }

        var services = new OverridingProvider(scope.ServiceProvider, overrides);
        await using var renderer = new HtmlRenderer(services, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ViewHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<ViewHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ViewHost.Module)] = module }));
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

            await act(ViewHost.View!, Html);
        });
    }

    private static async Task<string> GetAsync(WebApplicationFactory<Program> host, string path)
    {
        using HttpClient client = host.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}: {body[..Math.Min(body.Length, 3000)]}");

        return body;
    }

    private sealed record RunSeed(TaskRunTrigger Trigger, DateTime StartedAtUtc)
    {
        public TaskScheduleId? Schedule { get; init; }

        public int Items { get; init; } = 1;

        public Guid? Target { get; init; }

        public IReadOnlyList<string> Labels { get; init; } = [];

        public int Succeeded { get; init; }

        public int Failed { get; init; }

        public int Cancelled { get; init; }
    }

    /// <summary>The scope's services, except those the test replaces.</summary>
    private sealed class OverridingProvider(IServiceProvider inner, IReadOnlyDictionary<Type, object> overrides) : IKeyedServiceProvider
    {
        public object? GetService(Type serviceType) => overrides.TryGetValue(serviceType, out object? value) ? value : inner.GetService(serviceType);

        public object? GetKeyedService(Type serviceType, object? serviceKey) => ((IKeyedServiceProvider)inner).GetKeyedService(serviceType, serviceKey);

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
            ((IKeyedServiceProvider)inner).GetRequiredKeyedService(serviceType, serviceKey);
    }

    /// <summary>What the view sent, and whether each answer was an error.</summary>
    private sealed class SendLog
    {
        private readonly ConcurrentQueue<(object Request, bool Failed)> sends = new();

        public int Count => sends.Count;

        public void Add(object request, bool failed) => sends.Enqueue((request, failed));

        public IReadOnlyList<(TRequest Request, bool Failed)> Of<TRequest>() =>
            [.. sends.Where(send => send.Request is TRequest).Select(send => ((TRequest)send.Request, send.Failed))];

        public async Task WaitForAsync<TRequest>()
        {
            for (int attempt = 0; attempt < 50 && Of<TRequest>().Count == 0; attempt++)
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>Sends through the real dispatcher and records each request with whether its answer was an error.</summary>
    private sealed class RecordingDispatcher(ISerginDispatcher inner, SendLog log) : ISerginDispatcher
    {
        public async Task<ErrorOr.ErrorOr<TResponse>> SendAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, CancellationToken cancellationToken = default)
        {
            ErrorOr.ErrorOr<TResponse> answer = await inner.SendAsync(request, cancellationToken);
            log.Add(request, answer.IsError);

            return answer;
        }

        public async Task<ErrorOr.ErrorOr<Versioned<TResponse>>> SendVersionedAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default)
        {
            ErrorOr.ErrorOr<Versioned<TResponse>> answer = await inner.SendVersionedAsync(request, expected, cancellationToken);
            log.Add(request, answer.IsError);

            return answer;
        }
    }

    /// <summary>The providers the view's table, filters and dialogs render into, and a handle on the view.</summary>
    private sealed class ViewHost : ComponentBase
    {
        public static SerginTaskRunsView? View { get; set; }

        /// <summary>The page's <c>?module=</c> value, passed to the view.</summary>
        [Parameter]
        public string? Module { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<SerginTaskRunsView>(2);
            builder.AddComponentParameter(3, nameof(SerginTaskRunsView.Module), Module);
            builder.AddComponentReferenceCapture(4, instance => View = (SerginTaskRunsView)instance);
            builder.CloseComponent();
        }
    }

    /// <summary>A UI module with no pages and one entry, so the catalogue lists it in this order.</summary>
    private sealed class UiModule(string schema) : ISerginWebUiModule
    {
        private static Assembly NoComponents => typeof(SerginNavItem).Assembly;

        public string Name => schema;

        public string Schema => schema;

        public Assembly ApplicationAssembly => NoComponents;

        public Assembly ContractsAssembly => NoComponents;

        public Assembly ConfigurationsAssembly => NoComponents;

        public Assembly UiAssembly => NoComponents;

        public SerginNavGroup NavGroup => SerginNavGroups.Administration;

        public IReadOnlyCollection<SerginNavItem> NavItems => [new(TestText.Of("Only"), $"/{schema}/only", string.Empty)];

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }
}
