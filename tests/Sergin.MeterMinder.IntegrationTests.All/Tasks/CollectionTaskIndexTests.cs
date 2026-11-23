using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Tasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>
/// The starter's index path, deterministically: the store's advisory check always answers "nothing running", so a second
/// start of a running collection task reaches the insert, the unique index refuses it, and the starter answers
/// <c>Tasks.AlreadyRunning</c> with exactly one run written.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class CollectionTaskIndexTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    public async Task InitializeAsync()
    {
        host = TaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<ITaskEngine>(provider => provider.GetRequiredService<FakeTaskEngine>()));
                services.AddSingleton(ModuleConfigurationSource.FromTypesInSchema(TasksDbContext.Schema, typeof(SyncProbesCommandConfiguration)));
                services.AddTransient<IRequestHandler<SyncProbesCommand, ErrorOr<Success>>, SyncProbesCommandHandler>();
                BlindTheCollectionCheck(services);
            }));

        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task SecondStart_PastTheAdvisoryCheck_IsRefusedByTheIndex_AsAlreadyRunning()
    {
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.Company, ScheduleProbePermissions.Touch.Permission);
        Assert.False((await StartAsync(caller)).IsError);

        ErrorOr<TaskRunStartedResponse> second = await StartAsync(caller);

        Assert.True(second.IsError);
        Error error = Assert.Single(second.Errors);
        Assert.Equal(TaskErrors.AlreadyRunningCode, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Contains("North Region", error.Description, StringComparison.Ordinal);

        using IServiceScope scope = host.Services.CreateScope();
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        Assert.Equal(1, await context.TaskRuns.CountAsync());
        Assert.Equal(1, await context.TaskRunItems.CountAsync());
        Assert.Single(host.Services.GetRequiredService<FakeTaskEngine>().QueuedItems);
    }

    private async Task<ErrorOr<TaskRunStartedResponse>> StartAsync(IUserContext caller)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().StartTaskAsync(new SyncProbesCommand(), []);
    }

    private static void BlindTheCollectionCheck(IServiceCollection services)
    {
        ServiceDescriptor inner = services.Last(descriptor =>
            descriptor.ServiceType == typeof(ITaskStore) && Equals(descriptor.ServiceKey, TasksDbContext.Schema));
        services.Remove(inner);
        services.AddKeyedScoped<ITaskStore>(
            TasksDbContext.Schema,
            (provider, key) => new BlindCollectionCheckStore((ITaskStore)inner.KeyedImplementationFactory!(provider, key)));
    }

    /// <summary>The real store, except that it never sees a running collection run before the insert.</summary>
    private sealed class BlindCollectionCheckStore(ITaskStore inner) : ITaskStore
    {
        public string Schema => inner.Schema;

        public Task<bool> HasRunningCollectionRunAsync(string commandName, Guid scopeId, CancellationToken ct) => Task.FromResult(false);

        public Task<bool> TryClaimRunItemAsync(Guid runItemId, DateTime now, DateTime staleBefore, CancellationToken ct) =>
            inner.TryClaimRunItemAsync(runItemId, now, staleBefore, ct);

        public Task<TaskSchedule?> GetTaskAsync(TaskScheduleId id, bool ignoreScope, CancellationToken ct) => inner.GetTaskAsync(id, ignoreScope, ct);

        public (Guid ScopeId, string ScopePath) ScopeOf(TaskSchedule task) => inner.ScopeOf(task);

        public void AddTask(TaskSchedule task) => inner.AddTask(task);

        public void RemoveTask(TaskSchedule task) => inner.RemoveTask(task);

        public void AddRun(TaskRun run, IReadOnlyCollection<TaskRunItem> items) => inner.AddRun(run, items);

        public Task<bool> HasUnfinishedRunAsync(TaskScheduleId id, CancellationToken ct) => inner.HasUnfinishedRunAsync(id, ct);

        public Task<(TaskRun Run, TaskRunItem Item)?> GetRunItemAsync(Guid runItemId, CancellationToken ct) =>
            inner.GetRunItemAsync(runItemId, ct);

        public Task<TaskRun?> GetRunAsync(Guid runId, CancellationToken ct) => inner.GetRunAsync(runId, ct);

        public Task CompleteRunIfFinishedAsync(Guid runId, DateTime at, CancellationToken ct) => inner.CompleteRunIfFinishedAsync(runId, at, ct);

        public Task CancelRunAsync(Guid runId, DateTime at, CancellationToken ct) => inner.CancelRunAsync(runId, at, ct);

        public Task<IReadOnlyCollection<TaskScheduleTriggerState>> ListTriggersAsync(CancellationToken ct) => inner.ListTriggersAsync(ct);

        public Task<RunItemHeartbeat> TouchRunItemAsync(Guid runItemId, DateTime now, CancellationToken ct) => inner.TouchRunItemAsync(runItemId, now, ct);

        public Task SetRunItemProgressAsync(Guid runItemId, int percent, DateTime now, CancellationToken ct) =>
            inner.SetRunItemProgressAsync(runItemId, percent, now, ct);

        public Task<IReadOnlyCollection<Guid>> ListStaleRunItemsAsync(DateTime quietSince, CancellationToken ct) => inner.ListStaleRunItemsAsync(quietSince, ct);

        public Task<IReadOnlyCollection<Guid>> CancelAbandonedRunItemsAsync(DateTime quietSince, DateTime at, CancellationToken ct) =>
            inner.CancelAbandonedRunItemsAsync(quietSince, at, ct);

        public Task<IReadOnlyCollection<Guid>> ListRunsToCompleteAsync(DateTime quietSinceUtc, CancellationToken ct) =>
            inner.ListRunsToCompleteAsync(quietSinceUtc, ct);

        public Task<bool> RelinkStaleRunItemAsync(Guid runItemId, Guid engineJobId, DateTime now, DateTime quietSince, CancellationToken ct) =>
            inner.RelinkStaleRunItemAsync(runItemId, engineJobId, now, quietSince, ct);

        public Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct) => inner.PurgeAsync(olderThanUtc, ct);

        public Task<StartedRunSave> TrySaveStartedRunAsync(CancellationToken ct) => inner.TrySaveStartedRunAsync(ct);

        public Task SaveAsync(CancellationToken ct) => inner.SaveAsync(ct);
    }
}
