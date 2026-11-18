using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
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
/// A background command is started, never sent: sent inline it is refused before anything else runs; inside a run item the
/// same send passes; every other command still sends inline.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class BackgroundGuardTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string SlowName = "tst.probes.slow";

    private WebApplicationFactory<Program> host = default!;
    private RbacScenario tree = default!;

    private FakeScheduledTaskEngine Engine => host.Services.GetRequiredService<FakeScheduledTaskEngine>();

    private ProbeGate Gate => host.Services.GetRequiredService<ProbeGate>();

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeScheduledTaskEngine>();
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
                services.AddSingleton<ProbeGate>();
                services.AddSingleton<ProbeFailureSwitch>();
                services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
                services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
                services.AddTransient<IRequestHandler<SlowProbeCommand, ErrorOr<TouchProbeResponse>>, SlowProbeCommandHandler>();
                services.AddScoped<IValidator<SlowProbeCommand>, SlowProbeCommandValidator>();
            }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync()
    {
        Gate.Release();
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Inline_IsRefused()
    {
        ErrorOr<TouchProbeResponse> result = await tree.TrySendAsync(new SlowProbeCommand(new(Guid.NewGuid()), "inline"));

        Assert.True(result.IsError);
        Assert.Equal("ScheduledTasks.MustRunInBackground", result.FirstError.Code);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(0, Gate.Entered);
    }

    [Fact]
    public async Task InsideARunItem_Passes()
    {
        TestCaller user = await tree.CreateGrantedCallerAsync(tree.North, ScheduleProbePermissions.Touch.Permission);
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);
        Gate.Release();
        string arguments = ScheduledCommandFactory.ToArguments(new SlowProbeCommand(new(Guid.Empty), "slow"));
        Guid run = await StartRunAsync(user, tree.North, SlowName, arguments, probe.Id.Value);

        await Engine.DrainAsync(host.Services);

        ScheduledTaskRunItem item = Assert.Single(await ItemsAsync(run));
        Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status);
        Assert.Equal(1, Gate.Entered);
    }

    [Fact]
    public async Task OtherCommands_StillSendInline()
    {
        ScheduleProbe probe = await CreateProbeAsync(tree.Feeder1);

        // The scenario's default caller is the administrator at the root, so the permission check passes on the current scope.
        ErrorOr<TouchProbeResponse> result = await tree.TrySendAsync(new TouchProbeCommand(probe.Id, new ProbeNote("inline")));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
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

    /// <summary>A one-off run of <paramref name="commandName"/> for <paramref name="user"/>, one item per target, enqueued.</summary>
    private async Task<Guid> StartRunAsync(TestCaller user, Guid scopeId, string commandName, string arguments, params Guid[] targets)
    {
        using IServiceScope scope = host.Services.CreateScope();
        string path = (await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId))!;

        var run = ScheduledTaskRun.Start(
            new ScheduledRunDefinition(null, ScheduledRunTrigger.OneOff, user.Id, scopeId, path, commandName, arguments),
            targets.Length,
            DateTime.UtcNow);
        ScheduledTaskRunItem[] items = [.. targets.Select(target => ScheduledTaskRunItem.Create(run.Id, target, rootId: null, DateTime.UtcNow))];

        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        store.AddRun(run, items);
        await store.SaveAsync(CancellationToken.None);

        foreach (ScheduledTaskRunItem item in items)
        {
            await Engine.EnqueueRunItemAsync(new ScheduledRunItemReference(ScheduledTasksDbContext.Schema, item.Id), CancellationToken.None);
        }

        return run.Id;
    }

    private async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        return await context.ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
    }
}
