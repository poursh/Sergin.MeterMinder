using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.Revoke;
using Sergin.Administration.UserAccess.Domain.Users;
using Sergin.Administration.UserAccess.Domain.Users.Assignments;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Activity;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// A user's own runs across modules: every opted-in module's store is a source (here the real <c>dm</c> and the test
/// <c>tst</c>), merged newest first; only runs a person started (Background, Run now, Run once), only the caller's, in
/// any scope, running or finished within the window; a source that throws is left out.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ActivityDirectoryTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Tst = TasksDbContext.Schema;
    private const string Dm = "dm";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private static DateTime Window => DateTime.UtcNow.AddHours(-24);

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(new FakeTaskEngine()))));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ListsTheUsersRunsFromBothModules_NewestFirst()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(
            tree.North, ScheduleProbePermissions.Touch.Permission, DevicePermissions.ReadProfile.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Guid device = await CreateDeviceAsync(tree.Feeder1);

        TaskRunRef tstRun = await StartAsync(caller, new SlowProbeCommand(new(Guid.Empty), "slow"), new TaskTarget(probe.Id.Value));
        TaskRunRef dmRun = await StartAsync(caller, new ReadDeviceProfileCommand(new DeviceIntenralId(device)), new TaskTarget(device));

        IReadOnlyList<ActivityItem> activity = await ListAsync(caller.Id);

        Assert.Equal([dmRun, tstRun], activity.Select(item => item.Ref));
        Assert.Equal([Dm, Tst], activity.Select(item => item.Ref.Schema));
        Assert.All(activity, item => Assert.Equal(TaskRunTrigger.Manual, item.Trigger));
        Assert.All(activity, item => Assert.Equal(TaskRunStatus.Running, item.Status));
        Assert.Equal("dm.device.read-device-profile", activity[0].CommandName);
        Assert.Equal(1, activity[0].Unfinished);
        Assert.All(activity, item => Assert.True(item.Cancelable));
    }

    [Fact]
    public async Task ARunOfANotCancelableTask_IsListedAsNotCancelable()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        await StartAsync(caller, new FixedProbeCommand(new(Guid.Empty), "fixed"), new TaskTarget(probe.Id.Value));

        Assert.False(Assert.Single(await ListAsync(caller.Id)).Cancelable);
    }

    [Fact]
    public async Task CronRuns_AreNotActivity()
    {
        var user = new UserId(Guid.CreateVersion7());
        Guid oneOff = await SeedRunAsync(user, TaskRunTrigger.Once, DateTime.UtcNow);
        Guid runNow = await SeedRunAsync(user, TaskRunTrigger.ScheduleNow, DateTime.UtcNow);
        await SeedRunAsync(user, TaskRunTrigger.Schedule, DateTime.UtcNow);

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        Assert.Equal([runNow, oneOff], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task ASkippedRun_IsNotActivity()
    {
        // Run now losing the overlap rule records a Skipped run: complete at once, nothing sent, so not a success to show.
        var user = new UserId(Guid.CreateVersion7());
        Guid runNow = await SeedRunAsync(user, TaskRunTrigger.ScheduleNow, DateTime.UtcNow);

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Tst);
            var definition = new TaskRunDefinition(null, TaskRunTrigger.ScheduleNow, user, tree.North, "root", "tst.probes.touch", "{}");
            store.AddRun(TaskRun.Skipped(definition, DateTime.UtcNow), []);
            await store.SaveAsync(CancellationToken.None);
        }

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        Assert.Equal([runNow], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task OtherUsersRuns_AreNotListed()
    {
        var user = new UserId(Guid.CreateVersion7());
        Guid own = await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow);
        await SeedRunAsync(new UserId(Guid.CreateVersion7()), TaskRunTrigger.Manual, DateTime.UtcNow);

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        Assert.Equal([own], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task RunOutsideTheUsersCurrentReach_IsStillListed()
    {
        Guid role = await tree.CreateRoleAsync(tree.North, $"activity-{Guid.NewGuid():N}"[..20], inheritable: true, ScheduleProbePermissions.Touch.Permission);
        Guid user = await tree.CreateUserAsync("activity");
        Guid assignment = (await tree.AssignAsync(user, role, tree.North, includeSubtree: true)).Value.Id;
        var caller = new TestCaller { Id = new UserId(user) };
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        TaskRunRef run = await StartAsync(caller, new SlowProbeCommand(new(Guid.Empty), "slow"), new TaskTarget(probe.Id.Value));
        await tree.SendAsync(new RevokeAssignmentCommand(new UserInternalId(user), new RoleAssignmentId(assignment)));

        IReadOnlyList<ActivityItem> activity = await ListAsync(caller.Id);

        Assert.Equal([run], activity.Select(item => item.Ref));
    }

    [Fact]
    public async Task FinishedOlderThanTheWindow_IsNotListed()
    {
        var user = new UserId(Guid.CreateVersion7());
        DateTime longAgo = DateTime.UtcNow.AddHours(-25);
        Guid finished = await SeedRunAsync(user, TaskRunTrigger.Manual, longAgo, completed: true);
        Guid recent = await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow, completed: true);
        Guid running = await SeedRunAsync(user, TaskRunTrigger.Manual, longAgo);

        Assert.Contains(finished, (await ListAsync(user)).Select(item => item.Ref.RunId));

        await SetCompletedAtAsync(finished, longAgo);

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        Assert.Equal([recent, running], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task AThrowingSource_IsSkipped()
    {
        var user = new UserId(Guid.CreateVersion7());
        Guid own = await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow);
        await using WebApplicationFactory<Program> throwing = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<ITaskRunSource, ThrowingTaskRunSource>()));

        IReadOnlyList<ActivityItem> activity = await ListAsync(throwing, user);

        Assert.Equal([own], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task ASourceThatCannotBeBuilt_IsSkipped()
    {
        var user = new UserId(Guid.CreateVersion7());
        Guid own = await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow);
        await using WebApplicationFactory<Program> unbuildable = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<ITaskRunSource, UnbuildableTaskRunSource>()));

        IReadOnlyList<ActivityItem> activity = await ListAsync(unbuildable, user);

        Assert.Equal([own], activity.Select(item => item.Ref.RunId));
    }

    [Fact]
    public async Task ACancelledCaller_IsCancelled_NotAnsweredEmpty()
    {
        var user = new UserId(Guid.CreateVersion7());
        await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Services.GetRequiredService<ITaskRunDirectory>().ListActivityAsync(user, Window, cancelled.Token));
    }

    [Fact]
    public async Task OneTargetRun_CarriesTargetAndLabel_ManyTargetRunDoesNot()
    {
        var user = new UserId(Guid.CreateVersion7());
        var target = Guid.CreateVersion7();
        Guid many = await SeedRunAsync(
            user, TaskRunTrigger.Manual, DateTime.UtcNow.AddSeconds(-1), targets: [(Guid.CreateVersion7(), "A-1"), (Guid.CreateVersion7(), "A-2")]);
        Guid one = await SeedRunAsync(
            user, TaskRunTrigger.Manual, DateTime.UtcNow, completed: true, failedWith: "Some.Code", targets: [(target, "P-1")]);

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        ActivityItem single = activity.Single(item => item.Ref.RunId == one);
        Assert.Equal(target, single.TargetId);
        Assert.Equal("P-1", single.TargetLabel);
        Assert.Equal("Some.Code", single.FirstErrorCode);
        Assert.Equal("failed", single.FirstErrorDescription);
        Assert.Equal((1, 0, 1, 0), (single.Total, single.Succeeded, single.Failed, single.Unfinished));
        Assert.NotNull(single.CompletedAtUtc);

        ActivityItem several = activity.Single(item => item.Ref.RunId == many);
        Assert.Null(several.TargetId);
        Assert.Null(several.TargetLabel);
        Assert.Null(several.FirstErrorCode);
        Assert.Null(several.FirstErrorDescription);
        Assert.Equal((2, 2), (several.Total, several.Unfinished));
    }

    [Fact]
    public async Task OneItemRun_CarriesProgress_ManyItemRun_DoesNot()
    {
        var user = new UserId(Guid.CreateVersion7());
        Guid many = await SeedRunAsync(
            user, TaskRunTrigger.Manual, DateTime.UtcNow.AddSeconds(-1), targets: [(Guid.CreateVersion7(), null), (Guid.CreateVersion7(), null)], progress: 70);
        Guid one = await SeedRunAsync(user, TaskRunTrigger.Manual, DateTime.UtcNow, progress: 40);

        IReadOnlyList<ActivityItem> activity = await ListAsync(user);

        Assert.Equal(40, activity.Single(item => item.Ref.RunId == one).Progress);
        Assert.Null(activity.Single(item => item.Ref.RunId == many).Progress);
    }

    [Fact]
    public async Task CancelledCount_IsRead()
    {
        var user = new UserId(Guid.CreateVersion7());

        using (IServiceScope scope = host.Services.CreateScope())
        {
            ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Tst);
            var definition = new TaskRunDefinition(null, TaskRunTrigger.Manual, user, tree.North, "root", "tst.probes.touch", "{}");
            var run = TaskRun.Start(definition, 3, DateTime.UtcNow);
            TaskRunItem[] items = [.. Enumerable.Range(0, 3).Select(_ => TaskRunItem.Create(run.Id, Guid.CreateVersion7(), DateTime.UtcNow))];
            items[0].MarkSucceeded(DateTime.UtcNow);
            items[1].MarkCancelled(DateTime.UtcNow);
            items[2].MarkCancelled(DateTime.UtcNow);
            run.Complete(succeeded: 1, failed: 0, cancelled: 2, DateTime.UtcNow);
            store.AddRun(run, items);
            await store.SaveAsync(CancellationToken.None);
        }

        ActivityItem item = Assert.Single(await ListAsync(user));

        Assert.Equal((3, 1, 0, 2, 0), (item.Total, item.Succeeded, item.Failed, item.Cancelled, item.Unfinished));
    }

    [Fact]
    public async Task Query_ReturnsOnlyTheCallersRuns()
    {
        TestCaller first = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        TestCaller second = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        TaskRunRef firstRun = await StartAsync(first, new SlowProbeCommand(new(Guid.Empty), "slow"), new TaskTarget(probe.Id.Value));
        TaskRunRef secondRun = await StartAsync(second, new SlowProbeCommand(new(Guid.Empty), "slow"), new TaskTarget(probe.Id.Value));

        Assert.Equal([firstRun], (await QueryAsync(first)).Select(item => item.Ref));
        Assert.Equal([secondRun], (await QueryAsync(second)).Select(item => item.Ref));
    }

    private Task<IReadOnlyList<ActivityItem>> ListAsync(UserId user) => ListAsync(host, user);

    private static async Task<IReadOnlyList<ActivityItem>> ListAsync(WebApplicationFactory<Program> on, UserId user) =>
        await on.Services.GetRequiredService<ITaskRunDirectory>().ListActivityAsync(user, Window, CancellationToken.None);

    private async Task<IReadOnlyList<ActivityItem>> QueryAsync(IUserContext caller)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;

        ErrorOr<IReadOnlyList<ActivityItem>> result =
            await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(new GetMyActivityQuery(Window));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : string.Empty);
        return result.Value;
    }

    private async Task<TaskRunRef> StartAsync(IUserContext caller, Sergin.SharedKernel.Application.Commands.Types.ITaskCommand template, TaskTarget target)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        ErrorOr<TaskRunStartedResponse> started =
            await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().StartTaskAsync(template, [target]);

        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);
        return started.Value.Run;
    }

    private async Task<Guid> SeedRunAsync(
        UserId user,
        TaskRunTrigger trigger,
        DateTime startedAtUtc,
        bool completed = false,
        string? failedWith = null,
        IReadOnlyList<(Guid TargetId, string? Label)>? targets = null,
        int? progress = null)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ITaskStore store = scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(Tst);
        var definition = new TaskRunDefinition(null, trigger, user, tree.North, "root", "tst.probes.touch", "{}");
        targets ??= [(Guid.CreateVersion7(), null)];

        var run = TaskRun.Start(definition, targets.Count, startedAtUtc);
        TaskRunItem[] items = [.. targets.Select(target => TaskRunItem.Create(run.Id, target.TargetId, startedAtUtc, target.Label))];

        if (progress is { } percent)
        {
            foreach (TaskRunItem item in items)
            {
                item.SetProgress(percent);
            }
        }

        if (completed)
        {
            foreach (TaskRunItem item in items)
            {
                if (failedWith is null)
                {
                    item.MarkSucceeded(DateTime.UtcNow);
                }
                else
                {
                    item.MarkFailed(failedWith, "failed", DateTime.UtcNow);
                }
            }

            run.Complete(failedWith is null ? items.Length : 0, failedWith is null ? 0 : items.Length, 0, DateTime.UtcNow);
        }

        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        return run.Id;
    }

    private async Task SetCompletedAtAsync(Guid runId, DateTime completedAtUtc)
    {
        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        await context.Database.ExecuteSqlAsync($"UPDATE tst.task_runs SET completed_at_utc = {completedAtUtc} WHERE id = {runId}");
    }

    private async Task<ScheduleProbe> CreateProbeAsync(Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe;
    }

    /// <summary>A device in <paramref name="scopeId"/>, created as the host's development user.</summary>
    private async Task<Guid> CreateDeviceAsync(Guid scopeId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> manufacturer = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"manufacturer-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(manufacturer.IsError);
        ManufacturerId manufacturerId = new(manufacturer.Value.Id);

        ErrorOr<AddDeviceModelCommandResponse> model = await dispatcher.SendAtManufacturerVersionAsync(
            manufacturerId.Value, new AddDeviceModelCommand(manufacturerId, new DeviceModelName($"model-{Guid.CreateVersion7()}")));
        Assert.False(model.IsError);

        ErrorOr<CreateDeviceCommandResponse> device = await dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), new DeviceModelInternalId(model.Value.Id)));
        Assert.False(device.IsError, device.IsError ? device.FirstError.Description : string.Empty);
        return device.Value.Id;
    }
}

/// <summary>A module whose store cannot be read: the directory logs it and returns the others' runs.</summary>
internal sealed class ThrowingTaskRunSource : ITaskRunSource
{
    public string Schema => "broken";

    public Task<IReadOnlyList<ActivityItem>> ListActivityAsync(UserId user, DateTime finishedSinceUtc, int take, CancellationToken ct) =>
        throw new InvalidOperationException("The store is down.");
}

/// <summary>A source whose constructor throws: the directory logs it and still returns the others' runs.</summary>
internal sealed class UnbuildableTaskRunSource : ITaskRunSource
{
    public UnbuildableTaskRunSource() => throw new InvalidOperationException("The source cannot be built.");

    public string Schema => "unbuildable";

    public Task<IReadOnlyList<ActivityItem>> ListActivityAsync(UserId user, DateTime finishedSinceUtc, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActivityItem>>([]);
}
