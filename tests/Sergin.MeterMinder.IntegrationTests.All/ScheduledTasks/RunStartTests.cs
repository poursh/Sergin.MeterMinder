using System.Text.Json.Nodes;
using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// How a run begins: a cron firing is a Scheduled run for the task's delegating user in the task's scope, nothing for a
/// paused or deleted task, a Skipped run while the previous one is unfinished, and a copy of the command and arguments
/// so editing the task never rewrites history. A name or arguments that no longer fit fail per item, not the firing.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RunStartTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<FakeScheduledTaskEngine>();
            services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
            services.AddSingleton<ProbeFailureSwitch>();
            services.AddSingleton<ProbeSendRecorder>();
            services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
            services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
            services.AddTransient<IPipelineBehavior<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, RecordingProbeBehavior>();
        }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task Fire_StartsARun_ForTheDelegatingUser()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), first, second);

        await FireAsync(task);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        Assert.Equal(ScheduledRunTrigger.Scheduled, run.Trigger);
        Assert.Equal(task.DelegatingUserId, run.ActingUserId);
        Assert.Equal(tree.North, run.ScopeId);
        Assert.Equal(task.CommandName, run.CommandName);
        AssertSameJson(task.Arguments, run.Arguments);
        Assert.Equal(task.Targets.Count, run.Total);
        Assert.Equal(ScheduledRunStatus.Running, run.Status);

        IReadOnlyCollection<ScheduledTaskRunItem> items = await ItemsAsync(run.Id);
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.NotNull(item.EngineJobId));

        await Engine.DrainAsync(host.Services);

        Assert.Equal(ScheduledRunStatus.Completed, (await RunsAsync(task)).Single().Status);
        Assert.Equal("hello", (await ProbeAsync(first.Id)).LastNote);
    }

    [Fact]
    public async Task Fire_PausedTask_DoesNothing()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), probe);
        await ChangeTaskAsync(task.Id, loaded => loaded.Pause());

        await FireAsync(task);

        Assert.Empty(await RunsAsync(task));
    }

    [Fact]
    public async Task Fire_DeletedTask_DoesNothing()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), probe);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            IScheduledTaskStore store = Store(scope);
            store.RemoveTask((await store.GetTaskAsync(task.Id, ignoreScope: true, CancellationToken.None))!);
            await store.SaveAsync(CancellationToken.None);
        }

        await FireAsync(task);

        Assert.Empty(await RunsAsync(task));
    }

    [Fact]
    public async Task Fire_WhilePreviousUnfinished_RecordsSkipped()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), probe);

        await FireAsync(task);
        await FireAsync(task);

        IReadOnlyCollection<ScheduledTaskRun> runs = await RunsAsync(task);
        Assert.Equal(2, runs.Count);
        ScheduledTaskRun skipped = Assert.Single(runs, run => run.Status == ScheduledRunStatus.Skipped);
        Assert.Equal(0, skipped.Total);
        Assert.NotNull(skipped.CompletedAtUtc);
        Assert.Empty(await ItemsAsync(skipped.Id));
        Assert.Single(await ItemsAsync(runs.Single(run => run.Status == ScheduledRunStatus.Running).Id));

        // Once the first run finishes, the next firing starts a run again.
        await Engine.DrainAsync(host.Services);
        await FireAsync(task);

        Assert.Equal(1, (await RunsAsync(task)).Count(run => run.Status == ScheduledRunStatus.Running));
    }

    [Fact]
    public async Task Fire_AfterTaskEdited_HistoryKeepsOldArguments()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("before"), probe);
        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        string edited = Arguments("after");
        await ChangeTaskAsync(task.Id, loaded => loaded.Update(edited, [.. loaded.Targets], loaded.Cron, loaded.TimeZoneId, loaded.DelegatingUserId));

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        AssertSameJson(task.Arguments, run.Arguments);
        Assert.False(JsonNode.DeepEquals(JsonNode.Parse(edited), JsonNode.Parse(run.Arguments)));
    }

    [Fact]
    public async Task Fire_UnknownCommandName_FailsItems()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, "tst.probes.gone", Arguments("hello"), probe);

        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        Assert.Equal(ScheduledRunStatus.Completed, run.Status);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(ScheduledTaskErrors.UnknownCommand.Code, item.ErrorCode);
    }

    [Fact]
    public async Task Fire_BadArguments_FailsItems()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, """{"Note":42}""", probe);

        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        Assert.Equal(ScheduledRunStatus.Completed, run.Status);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(ScheduledTaskErrors.ArgumentsInvalid.Code, item.ErrorCode);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task Fire_DelegatingUserLostReach_StillLoads_ItemsForbidden()
    {
        // Holds the permission on North only; the task and its probe live in South, out of the user's reach.
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.South);
        ScheduledTask task = await CreateTaskAsync(user, tree.South, TouchName, Arguments("hello"), probe);

        await FireAsync(task);
        await Engine.DrainAsync(host.Services);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        Assert.Equal(tree.South, run.ScopeId);
        Assert.Equal(ScheduledRunStatus.Completed, run.Status);
        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run.Id));
        Assert.Equal(ScheduledRunItemStatus.Failed, item.Status);
        Assert.Equal(Error.Forbidden().Code, item.ErrorCode);
        Assert.Null((await ProbeAsync(probe.Id)).LastNote);
    }

    // jsonb stores arguments normalized (spacing), so equality is of the JSON, not the text.
    [Fact]
    public async Task Fire_EngineThrows_FailsItems_CompletesRun_NextFireStartsAgain()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe first = await CreateProbeAsync(tree.Feeder1);
        ScheduleProbe second = await CreateProbeAsync(tree.Feeder2);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), first, second);
        Engine.FailNextCall = true;

        await FireAsync(task);

        ScheduledTaskRun run = Assert.Single(await RunsAsync(task));
        Assert.Equal(ScheduledRunStatus.Completed, run.Status);
        Assert.Equal(run.Total, run.Failed);
        Assert.All(await ItemsAsync(run.Id), item => Assert.Equal(ScheduledTaskErrors.EngineUnavailable.Code, item.ErrorCode));

        await FireAsync(task);

        Assert.Equal(1, (await RunsAsync(task)).Count(one => one.Status == ScheduledRunStatus.Running));
        Assert.DoesNotContain(await RunsAsync(task), one => one.Status == ScheduledRunStatus.Skipped);
    }

    [Fact]
    public async Task Fire_Concurrently_StartsOneRun_SkipsTheOther()
    {
        TestCaller user = await TouchCallerAsync(tree.North);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        ScheduledTask task = await CreateTaskAsync(user, tree.North, TouchName, Arguments("hello"), probe);

        await Task.WhenAll(FireAsync(task), FireAsync(task));

        IReadOnlyCollection<ScheduledTaskRun> runs = await RunsAsync(task);
        Assert.Equal(2, runs.Count);
        Assert.Equal(1, runs.Count(run => run.Status == ScheduledRunStatus.Running));
        Assert.Equal(1, runs.Count(run => run.Status == ScheduledRunStatus.Skipped));
        Assert.Single(Engine.QueuedItems);
    }

    private static void AssertSameJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"{expected} != {actual}");

    private static string Arguments(string note) =>
        ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote(note)));

    private static IScheduledTaskStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);

    private Task<TestCaller> TouchCallerAsync(Guid scopeId) =>
        tree.CreateGrantedCallerAsync(scopeId, ScheduleProbePermissions.Touch.Permission);

    private async Task FireAsync(ScheduledTask task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScheduledTaskRunner>()
            .FireAsync(ScheduledTasksDbContext.Schema, task.Id, CancellationToken.None);
    }

    private async Task<ScheduledTask> CreateTaskAsync(TestCaller user, Guid scopeId, string commandName, string arguments, params ScheduleProbe[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        var task = ScheduledTask.Create(
            commandName, arguments, [.. targets.Select(probe => new ScheduledTaskTarget(probe.Id.Value, null))], "0 * * * *", "UTC", user.Id);
        IScheduledTaskStore store = Store(scope);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task;
    }

    private async Task ChangeTaskAsync(ScheduledTaskId id, Action<ScheduledTask> change)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IScheduledTaskStore store = Store(scope);
        change((await store.GetTaskAsync(id, ignoreScope: true, CancellationToken.None))!);
        await store.SaveAsync(CancellationToken.None);
    }

    private async Task<ScheduleProbe> CreateProbeAsync(Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRun>> RunsAsync(ScheduledTask task)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRuns.AsNoTracking().Where(run => run.TaskId == task.Id).OrderBy(run => run.StartedAtUtc).ToListAsync();
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }

    private async Task<ScheduleProbe> ProbeAsync(ScheduleProbeId id)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.Probes.AsNoTracking().IgnoreQueryFilters().SingleAsync(probe => probe.Id == id);
    }
}
