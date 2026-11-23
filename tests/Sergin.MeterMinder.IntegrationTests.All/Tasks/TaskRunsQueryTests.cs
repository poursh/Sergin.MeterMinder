using System.Globalization;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Queries;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using ErrorOrType = ErrorOr.ErrorType;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The module's run list behind the Task runs page: every trigger, newest first, paged, filtered by task, trigger,
/// person, start time and shown status, and limited to the runs inside the caller's reach for the manage permission
/// (a schedule's run through its live schedule, any other run through its own scope), which are neither listed nor
/// counted otherwise.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskRunsQueryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";
    private const string OtherName = "tst.probes.other";
    private const string Schema = TasksDbContext.Schema;

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;
    private TestCaller manager = default!;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
        }));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        manager = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ListsEveryTrigger_NewestFirst()
    {
        DateTime now = DateTime.UtcNow;
        TaskSchedule schedule = await CreateTaskAsync(tree.North);
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-40)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-30)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.ScheduleNow, now.AddMinutes(-20)) { Schedule = schedule, Settle = Settled(succeeded: 1) });
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Schedule, now.AddMinutes(-10)) { Schedule = schedule });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North);

        Assert.Equal(
            [TaskRunTrigger.Schedule, TaskRunTrigger.ScheduleNow, TaskRunTrigger.Once, TaskRunTrigger.Manual],
            runs.Data.Select(item => item.Trigger));
        Assert.Equal(4, runs.Total);
        Assert.All(runs.Data, item => Assert.Equal(TouchName, item.CommandName));
    }

    [Fact]
    public async Task Paged_TotalCountsEveryMatch()
    {
        DateTime now = DateTime.UtcNow;
        List<Guid> newestFirst = [];

        for (int minutes = 10; minutes <= 50; minutes += 10)
        {
            newestFirst.Add(await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-minutes))));
        }

        ListQueryResponse<TaskRunsListItem> second = await ListAsync(manager, tree.North, Paggination.Create(2, 2));

        Assert.Equal(5, second.Total);
        Assert.Equal(newestFirst.Skip(2).Take(2), second.Data.Select(item => item.Id));
    }

    [Fact]
    public async Task TaskFilter()
    {
        DateTime now = DateTime.UtcNow;
        Guid touch = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-20)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { CommandName = OtherName });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North, filtering: Filter(TaskRunListFields.Task, TouchName));

        Assert.Equal([touch], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
    }

    [Fact]
    public async Task TriggerFilter()
    {
        DateTime now = DateTime.UtcNow;
        Guid manual = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-20)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)));

        ListQueryResponse<TaskRunsListItem> runs =
            await ListAsync(manager, tree.North, filtering: Filter(TaskRunListFields.Trigger, nameof(TaskRunTrigger.Manual)));

        Assert.Equal([manual], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
    }

    [Fact]
    public async Task StartedByFilter()
    {
        DateTime now = DateTime.UtcNow;
        TestCaller other = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-20)));
        Guid byOther = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-10)) { ActingUserId = other.Id });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(
            manager, tree.North, filtering: Filter(TaskRunListFields.StartedBy, other.Id.Value.ToString()));

        Assert.Equal([byOther], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
        Assert.Equal(other.Id.Value, Assert.Single(runs.Data).ActingUserId);
    }

    [Fact]
    public async Task StartedBetweenFilter()
    {
        DateTime now = DateTime.UtcNow;
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddHours(-3)));
        Guid inRange = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddHours(-2)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddHours(-1)));

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(
            manager, tree.North, filtering: Range(TaskRunListFields.StartedAt, now.AddMinutes(-150), now.AddMinutes(-90)));

        Assert.Equal([inRange], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
    }

    [Fact]
    public async Task StatusFilter_MatchesTheShownStatus_ForEveryOutcome()
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<Guid, (TaskRunStatus Status, RunOutcome Expected)> seeded = [];

        async Task SeedAsync(RunSeed seed, TaskRunStatus status, RunOutcome expected) =>
            seeded.Add(await AddRunAsync(seed), (status, expected));

        await SeedAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-60)), TaskRunStatus.Running, RunOutcome.Running);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-50)) { Settle = Settled(succeeded: 1) },
            TaskRunStatus.Completed,
            RunOutcome.Succeeded);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-40)) { Items = 2, Settle = Settled(succeeded: 1, failed: 1) },
            TaskRunStatus.Completed,
            RunOutcome.PartlyFailed);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-30)) { Settle = Settled(failed: 1) },
            TaskRunStatus.Completed,
            RunOutcome.Failed);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-20)) { Settle = Settled(cancelled: 1) },
            TaskRunStatus.Completed,
            RunOutcome.Cancelled);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-15)) { Items = 2, Settle = Settled(succeeded: 1, cancelled: 1) },
            TaskRunStatus.Completed,
            RunOutcome.Cancelled);
        await SeedAsync(
            new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { Skipped = true },
            TaskRunStatus.Skipped,
            RunOutcome.Skipped);

        ListQueryResponse<TaskRunsListItem> all = await ListAsync(manager, tree.North);
        Assert.Equal(7, all.Total);
        Assert.All(all.Data, item =>
        {
            (TaskRunStatus status, RunOutcome expected) = seeded[item.Id];
            Assert.Equal(expected, item.Outcome);
            Assert.Equal(RunOutcomes.Of(status, item.Unfinished, item.Succeeded, item.Failed, item.Cancelled), item.Outcome);
        });

        foreach (RunOutcome outcome in Enum.GetValues<RunOutcome>())
        {
            ListQueryResponse<TaskRunsListItem> filtered =
                await ListAsync(manager, tree.North, filtering: Filter(TaskRunListFields.Status, outcome.ToString()));

            Guid[] expected = [.. seeded.Where(pair => pair.Value.Expected == outcome).Select(pair => pair.Key)];
            Assert.Equal(expected.Order(), filtered.Data.Select(item => item.Id).Order());
            Assert.Equal(expected.Length, filtered.Total);
            Assert.All(filtered.Data, item => Assert.Equal(outcome, item.Outcome));
        }
    }

    [Fact]
    public async Task DeclaredSort_OrdersRows()
    {
        DateTime now = DateTime.UtcNow;
        Guid a = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-30)) { CommandName = "tst.probes.b" });
        Guid b = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-20)) { CommandName = "tst.probes.c" });
        Guid c = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { CommandName = "tst.probes.a" });

        ListQueryResponse<TaskRunsListItem> byTask =
            await ListAsync(manager, tree.North, sorting: Sorting.Create($$"""[{"id":"{{TaskRunListFields.Task}}","desc":false}]"""));
        Assert.Equal([c, a, b], byTask.Data.Select(item => item.Id));

        ListQueryResponse<TaskRunsListItem> byStart =
            await ListAsync(manager, tree.North, sorting: Sorting.Create($$"""[{"id":"{{TaskRunListFields.StartedAt}}","desc":false}]"""));
        Assert.Equal([a, b, c], byStart.Data.Select(item => item.Id));
    }

    [Fact]
    public async Task Actors_AreTheDistinctActingUsersInReach()
    {
        DateTime now = DateTime.UtcNow;
        TestCaller other = await tree.CreateGrantedCallerAsync(tree.North, TaskPermissions.Manage.Permission);
        TestCaller southern = await tree.CreateGrantedCallerAsync(tree.South, TaskPermissions.Manage.Permission);
        TaskSchedule hidden = await CreateTaskAsync(tree.South);
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-30)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-20)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { ActingUserId = other.Id });
        await AddRunAsync(new RunSeed(tree.South, TaskRunTrigger.Once, now.AddMinutes(-5)) { ActingUserId = southern.Id });
        await AddRunAsync(new RunSeed(tree.South, TaskRunTrigger.Schedule, now.AddMinutes(-4)) { Schedule = hidden });

        ErrorOr<IReadOnlyCollection<Guid>> result = await SendAsync(manager, tree.North, new GetTaskRunActorsQuery(Schema));

        Assert.False(result.IsError);
        Assert.Equal(new[] { manager.Id.Value, other.Id.Value }.Order(), result.Value.Order());
    }

    [Fact]
    public async Task Actors_WithoutManage_AreForbidden()
    {
        TestCaller toucher = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

        Assert.Equal(ErrorOrType.Forbidden, (await SendAsync(toucher, tree.North, new GetTaskRunActorsQuery(Schema))).FirstError.Type);
    }

    [Fact]
    public async Task OneItemRun_CarriesTargetLabelAndProgress_BulkRunDoesNot()
    {
        DateTime now = DateTime.UtcNow;
        var target = Guid.CreateVersion7();
        Guid one = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-20))
        {
            Target = target,
            TargetLabel = "Probe A",
            Settle = (_, items) =>
            {
                items[0].MarkRunning(DateTime.UtcNow);
                items[0].SetProgress(40);
            },
        });
        Guid bulk = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-10)) { Items = 2, TargetLabel = "Probe" });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North);

        TaskRunsListItem single = Assert.Single(runs.Data, item => item.Id == one);
        Assert.Equal(target, single.TargetId);
        Assert.Equal("Probe A", single.TargetLabel);
        Assert.Equal(40, single.Progress);
        Assert.Equal(1, single.Total);
        Assert.Equal(1, single.Unfinished);
        Assert.Equal(RunOutcome.Running, single.Outcome);

        TaskRunsListItem many = Assert.Single(runs.Data, item => item.Id == bulk);
        Assert.Null(many.TargetId);
        Assert.Null(many.TargetLabel);
        Assert.Null(many.Progress);
        Assert.Equal(2, many.Total);
        Assert.Equal(2, many.Unfinished);
    }

    [Fact]
    public async Task CronRun_CarriesItsSchedulesCron_OneOffRunNone()
    {
        DateTime now = DateTime.UtcNow;
        TaskSchedule schedule = await CreateTaskAsync(tree.North);
        Guid cron = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Schedule, now.AddMinutes(-20)) { Schedule = schedule });
        Guid oneOff = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)));

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North);

        TaskRunsListItem scheduled = Assert.Single(runs.Data, item => item.Id == cron);
        Assert.Equal(schedule.Id.Value, scheduled.ScheduleId);
        Assert.Equal("0 * * * *", scheduled.ScheduleCron);
        Assert.Equal("UTC", scheduled.ScheduleTimeZoneId);
        Assert.Equal(schedule.DelegatingUserId.Value, scheduled.ActingUserId);

        TaskRunsListItem once = Assert.Single(runs.Data, item => item.Id == oneOff);
        Assert.Null(once.ScheduleId);
        Assert.Null(once.ScheduleCron);
        Assert.Null(once.ScheduleTimeZoneId);
    }

    [Fact]
    public async Task OutOfReachRun_NeitherListedNorCounted()
    {
        DateTime now = DateTime.UtcNow;
        TaskSchedule southern = await CreateTaskAsync(tree.South);
        Guid northern = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-30)));
        await AddRunAsync(new RunSeed(tree.South, TaskRunTrigger.Once, now.AddMinutes(-20)));
        await AddRunAsync(new RunSeed(tree.South, TaskRunTrigger.Schedule, now.AddMinutes(-10)) { Schedule = southern });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North);

        Assert.Equal([northern], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);

        // Filtering for what only South holds finds nothing and counts nothing.
        ListQueryResponse<TaskRunsListItem> scheduled =
            await ListAsync(manager, tree.North, filtering: Filter(TaskRunListFields.Trigger, nameof(TaskRunTrigger.Schedule)));
        Assert.Empty(scheduled.Data);
        Assert.Equal(0, scheduled.Total);
    }

    [Fact]
    public async Task DeletedSchedulesRuns_AreHidden()
    {
        DateTime now = DateTime.UtcNow;
        TaskSchedule schedule = await CreateTaskAsync(tree.North);
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Schedule, now.AddMinutes(-20)) { Schedule = schedule });
        Guid oneOff = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)));

        Assert.Equal(2, (await ListAsync(manager, tree.North)).Total);

        await DeleteTaskAsync(schedule);

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North);
        Assert.Equal([oneOff], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
    }

    [Fact]
    public async Task OneOffRun_ReachedByItsOwnScope()
    {
        Guid run = await AddRunAsync(new RunSeed(tree.Feeder1, TaskRunTrigger.Manual, DateTime.UtcNow.AddMinutes(-10)));
        TestCaller feeder1Manager = await tree.CreateGrantedCallerAsync(tree.Feeder1, TaskPermissions.Manage.Permission);
        TestCaller feeder2Manager = await tree.CreateGrantedCallerAsync(tree.Feeder2, TaskPermissions.Manage.Permission);

        Assert.Equal([run], (await ListAsync(manager, tree.North)).Data.Select(item => item.Id));
        Assert.Equal([run], (await ListAsync(feeder1Manager, tree.Feeder1)).Data.Select(item => item.Id));

        ListQueryResponse<TaskRunsListItem> elsewhere = await ListAsync(feeder2Manager, tree.Feeder2);
        Assert.Empty(elsewhere.Data);
        Assert.Equal(0, elsewhere.Total);
    }

    [Fact]
    public async Task WithoutManage_IsForbidden()
    {
        TestCaller toucher = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);

        ErrorOr<ListQueryResponse<TaskRunsListItem>> result =
            await SendAsync(toucher, tree.North, new GetTaskRunsQuery(Schema, Paggination.Default));

        Assert.Equal(ErrorOrType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task UnknownSchema_IsRefused()
    {
        ErrorOr<ListQueryResponse<TaskRunsListItem>> result =
            await SendAsync(manager, tree.North, new GetTaskRunsQuery("tst\"; DROP TABLE x; --", Paggination.Default));

        Assert.True(result.IsError);
        Assert.Equal(ErrorOrType.Validation, result.FirstError.Type);
        Assert.Equal("Tasks.UnknownSchema", result.FirstError.Code);
    }

    [Fact]
    public async Task UndeclaredSort_IsAValidationError()
    {
        ErrorOr<ListQueryResponse<TaskRunsListItem>> result = await SendAsync(
            manager,
            tree.North,
            new GetTaskRunsQuery(Schema, Paggination.Default, sorting: Sorting.Create($$"""[{"id":"{{TaskRunListFields.Trigger}}","desc":false}]""")));

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorOrType.Validation, error.Type);
        Assert.Equal("Sorting", error.Code);
    }

    [Fact]
    public async Task AggregateFilter_ListsTheRootsTasks_ByCatalogNotByName()
    {
        DateTime now = DateTime.UtcNow;
        Guid touch = await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-5)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { CommandName = OtherName });

        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North, filtering: Filter(TaskRunListFields.Aggregate, "schedule-probe"));

        Assert.Equal([touch], runs.Data.Select(item => item.Id));
        Assert.Equal(1, runs.Total);
    }

    [Fact]
    public async Task AggregateFilter_AndTaskFilter_BothApply()
    {
        DateTime now = DateTime.UtcNow;
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Manual, now.AddMinutes(-5)));
        await AddRunAsync(new RunSeed(tree.North, TaskRunTrigger.Once, now.AddMinutes(-10)) { CommandName = OtherName });

        var both = Filtering.Create(
            $$"""[{"id":"{{TaskRunListFields.Aggregate}}","value":"schedule-probe","mode":"equals"},{"id":"{{TaskRunListFields.Task}}","value":"{{OtherName}}","mode":"equals"}]""");
        ListQueryResponse<TaskRunsListItem> runs = await ListAsync(manager, tree.North, filtering: both);

        Assert.Empty(runs.Data);
        Assert.Equal(0, runs.Total);
    }

    [Fact]
    public async Task AggregateFilter_UnknownAggregate_IsAValidationError()
    {
        ErrorOr<ListQueryResponse<TaskRunsListItem>> result = await SendAsync(
            manager,
            tree.North,
            new GetTaskRunsQuery(Schema, Paggination.Default, filtering: Filter(TaskRunListFields.Aggregate, "nope")));

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorOrType.Validation, error.Type);
        Assert.Equal("Filtering", error.Code);
    }

    [Fact]
    public void EveryListField_HasAFieldText()
    {
        TextCatalog catalog = host.Services.GetRequiredService<TextCatalog>();

        Assert.All(TaskRunListFields.Set.Fields, field =>
        {
            string key = $"common.tasks.run-list.fields.{Kebab(field.Id)}.label";
            Assert.True(catalog.TryGet(key, out _), $"'{field.Id}' has no '{key}' text.");
        });
    }

    private static string Kebab(string camel) =>
        string.Concat(camel.Select(character => char.IsUpper(character)
            ? "-" + char.ToLowerInvariant(character)
            : character.ToString()));

    private async Task<ListQueryResponse<TaskRunsListItem>> ListAsync(
        IUserContext caller, Guid currentScope, Paggination? paggination = null, Filtering? filtering = null, Sorting? sorting = null)
    {
        ErrorOr<ListQueryResponse<TaskRunsListItem>> result = await SendAsync(
            caller, currentScope, new GetTaskRunsQuery(Schema, paggination ?? Paggination.Create(100, 1), filtering: filtering, sorting: sorting));

        Assert.False(result.IsError, result.IsError ? $"{result.FirstError.Code}: {result.FirstError.Description}" : string.Empty);

        return result.Value;
    }

    private static Filtering Filter(string id, string value) =>
        Filtering.Create($$"""[{"id":"{{id}}","value":"{{value}}","mode":"equals"}]""");

    private static Filtering Range(string id, DateTime from, DateTime to) =>
        Filtering.Create(
            $$"""[{"id":"{{id}}","value":["{{from.ToString("O", CultureInfo.InvariantCulture)}}","{{to.ToString("O", CultureInfo.InvariantCulture)}}"],"mode":"between"}]""");

    private static Action<TaskRun, IReadOnlyList<TaskRunItem>> Settled(int succeeded = 0, int failed = 0, int cancelled = 0) =>
        (run, items) =>
        {
            DateTime at = DateTime.UtcNow;
            int index = 0;

            for (int i = 0; i < succeeded; i++)
            {
                items[index++].MarkSucceeded(at);
            }

            for (int i = 0; i < failed; i++)
            {
                items[index++].MarkFailed("Probe.Failed", "The probe failed.", at);
            }

            for (int i = 0; i < cancelled; i++)
            {
                items[index++].MarkCancelled(at);
            }

            run.Complete(succeeded, failed, cancelled, at);
        };

    private async Task<ErrorOr<T>> SendAsync<T>(IUserContext caller, Guid currentScope, IRequest<ErrorOr<T>> request)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(currentScope);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
    }

    private static ITaskStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Schema);

    private async Task<TaskSchedule> CreateTaskAsync(Guid scopeId)
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));

        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        var task = TaskSchedule.Create(TouchName, arguments, [new TaskScheduleTarget(Guid.CreateVersion7())], "0 * * * *", "UTC", user.Id);
        ITaskStore store = Store(scope);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task;
    }

    private async Task DeleteTaskAsync(TaskSchedule task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = Store(scope);
        store.RemoveTask((await store.GetTaskAsync(task.Id, ignoreScope: true, CancellationToken.None))!);
        await store.SaveAsync(CancellationToken.None);
    }

    /// <summary>Saves one run as seeded: a schedule's run acts for its delegating user, as a firing does.</summary>
    private async Task<Guid> AddRunAsync(RunSeed seed)
    {
        string arguments = TaskCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("seeded")));

        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(seed.ScopeId))!;
        TaskRunDefinition definition = new(
            seed.Schedule?.Id,
            seed.Trigger,
            seed.ActingUserId ?? seed.Schedule?.DelegatingUserId ?? manager.Id,
            seed.ScopeId,
            path,
            seed.CommandName,
            arguments);

        ITaskStore store = Store(scope);

        if (seed.Skipped)
        {
            var skipped = TaskRun.Skipped(definition, seed.StartedAtUtc);
            store.AddRun(skipped, []);
            await store.SaveAsync(CancellationToken.None);

            return skipped.Id;
        }

        var run = TaskRun.Start(definition, seed.Items, seed.StartedAtUtc);
        IReadOnlyList<TaskRunItem> items =
        [
            .. Enumerable.Range(0, seed.Items).Select(index => TaskRunItem.Create(
                run.Id,
                index == 0 && seed.Target is { } target ? target : Guid.CreateVersion7(),
                DateTime.UtcNow,
                LabelOf(index))),
        ];
        seed.Settle?.Invoke(run, items);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        return run.Id;

        string? LabelOf(int index)
        {
            if (seed.TargetLabel is null)
            {
                return null;
            }

            return seed.Items == 1 ? seed.TargetLabel : $"{seed.TargetLabel} {index}";
        }
    }

    private sealed record RunSeed(Guid ScopeId, TaskRunTrigger Trigger, DateTime StartedAtUtc)
    {
        public TaskSchedule? Schedule { get; init; }

        public UserId? ActingUserId { get; init; }

        public string CommandName { get; init; } = TouchName;

        public int Items { get; init; } = 1;

        public Guid? Target { get; init; }

        public string? TargetLabel { get; init; }

        public bool Skipped { get; init; }

        public Action<TaskRun, IReadOnlyList<TaskRunItem>>? Settle { get; init; }
    }
}
