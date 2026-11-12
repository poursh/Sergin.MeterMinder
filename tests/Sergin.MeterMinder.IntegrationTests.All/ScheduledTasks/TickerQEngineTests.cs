using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using TickerQ.DependencyInjection;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The real engine, end to end: a cron trigger fires a run, a run item is a TickerQ job, and a transient failure is
/// tried again through a new job after Sergin's own interval. The end-to-end tests need a TickerQ licence and skip
/// without one; the cron check needs only TickerQ's parser.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TickerQEngineTests(SerginWebApiFactory<Program> factory)
{
    private const string Schema = ScheduledTasksDbContext.Schema;

    [Fact]
    public async Task InvalidCron_IsInvalid()
    {
        string licence = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tqlicense");
        await using ServiceProvider provider = TickerQTestStore.BareProvider(TickerQTestStore.ConnectionString(factory.Services), licence);
        IScheduledTaskEngine engine = provider.GetRequiredService<IScheduledTaskEngine>();

        Assert.IsType<TickerQScheduledTaskEngine>(engine);
        Assert.False(engine.IsValidCron("not a cron", "UTC"));
        Assert.False(engine.IsValidCron("0 2 * * *", "Nowhere/Atlantis"));
        Assert.False(engine.IsValidCron("%cron%", "UTC"));
        Assert.True(engine.IsValidCron("0 2 * * *", "Asia/Tehran"));
        Assert.True(engine.IsValidCron("*/5 * * * * *", "UTC"));
    }

    [Fact]
    public async Task NextOccurrences_AreTheZonesLocalTimes_AndAgreeWithIsValidCron()
    {
        string licence = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tqlicense");
        await using ServiceProvider provider = TickerQTestStore.BareProvider(TickerQTestStore.ConnectionString(factory.Services), licence);
        IScheduledTaskEngine engine = provider.GetRequiredService<IScheduledTaskEngine>();
        var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var after = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        IReadOnlyList<DateTimeOffset> times = engine.NextOccurrences("0 2 * * *", "Asia/Tehran", after, 3);

        Assert.Equal(3, times.Count);
        Assert.All(times, time =>
        {
            Assert.Equal(new TimeSpan(2, 0, 0), TimeZoneInfo.ConvertTime(time, tehran).TimeOfDay);
            Assert.True(time > after);
        });
        Assert.Equal(times.OrderBy(time => time), times);
        Assert.Equal(times.Distinct().Count(), times.Count);

        // Six fields mean seconds first, as in IsValidCron.
        Assert.Equal(2, engine.NextOccurrences("*/30 * * * * *", "UTC", after, 2).Count);

        foreach ((string cron, string zone) in new[]
        {
            ("0 2 * * *", "Asia/Tehran"), ("*/5 * * * * *", "UTC"), ("0 0 1 * *", "UTC"), ("0 8 * * 1", "UTC"),
            ("not a cron", "UTC"), ("0 2 * * *", "Nowhere/Atlantis"), ("%cron%", "UTC"), ("61 * * * *", "UTC"), (string.Empty, "UTC"),
        })
        {
            Assert.Equal(engine.IsValidCron(cron, zone), engine.NextOccurrences(cron, zone, after, 1).Count == 1);
        }
    }

    [RequiresTickerQLicenseFact]
    public async Task CronFire_RunsTheCommand_EndToEnd()
    {
        await using Fixture fixture = await Fixture.StartAsync(factory);
        ScheduleProbe probe = await fixture.CreateProbeAsync();

        ErrorOr<CreateScheduledTaskCommandResponse> created = await fixture.SendAsync(
            new CreateScheduledTaskCommand(Schema, Touch("cron"), [new(probe.Id.Value, null)], "* * * * *", "UTC"));
        Assert.False(created.IsError, created.IsError ? created.FirstError.Code : string.Empty);
        var id = new ScheduledTaskId(created.Value.Id);

        ScheduledTaskRun run = await fixture.EventuallyAsync(
            async context => await context.ScheduledTaskRuns.AsNoTracking()
                .FirstOrDefaultAsync(one => one.TaskId == id && one.Status == ScheduledRunStatus.Completed),
            TimeSpan.FromSeconds(90));

        IReadOnlyCollection<ScheduledTaskRunItem> items = await fixture.ItemsAsync(run.Id);
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(ScheduledRunItemStatus.Succeeded, item.Status));
        Assert.Equal(1, run.Succeeded);
    }

    [RequiresTickerQLicenseFact]
    public async Task RunOnce_EndToEnd()
    {
        await using Fixture fixture = await Fixture.StartAsync(factory);
        ScheduleProbe probe = await fixture.CreateProbeAsync();

        ErrorOr<ScheduledRunStartedResponse> started = await fixture.SendAsync(
            new RunCommandOnceCommand(Schema, Touch("once"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRunItem item = await fixture.EventuallyAsync(
            async context => await context.ScheduledTaskRunItems.AsNoTracking()
                .FirstOrDefaultAsync(one => one.RunId == started.Value.RunId && one.Status == ScheduledRunItemStatus.Succeeded),
            TimeSpan.FromSeconds(30));

        Assert.Equal(1, item.Attempts);
        Assert.NotNull(item.EngineJobId);
    }

    [RequiresTickerQLicenseFact]
    public async Task TransientFailure_IsRetriedThroughTickerQ()
    {
        await using Fixture fixture = await Fixture.StartAsync(factory, retryIntervalSeconds: 1);
        ScheduleProbe probe = await fixture.CreateProbeAsync();
        fixture.Failures.ThrowTimes = 1;

        ErrorOr<ScheduledRunStartedResponse> started = await fixture.SendAsync(
            new RunCommandOnceCommand(Schema, Touch("retried"), [new(probe.Id.Value, null)]));
        Assert.False(started.IsError, started.IsError ? started.FirstError.Code : string.Empty);

        ScheduledTaskRunItem item = await fixture.EventuallyAsync(
            async context => await context.ScheduledTaskRunItems.AsNoTracking()
                .FirstOrDefaultAsync(one => one.RunId == started.Value.RunId && one.Status == ScheduledRunItemStatus.Succeeded),
            TimeSpan.FromSeconds(30));

        Assert.Equal(2, item.Attempts);
    }

    private static TouchProbeCommand Touch(string note) => new(new(Guid.Empty), new ProbeNote(note));

    /// <summary>A licensed host over real TickerQ: its tables built first, then the host, then the engine activated.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> host;
        private RbacScenario tree = default!;
        private TestCaller manager = default!;

        private Fixture(WebApplicationFactory<Program> host) => this.host = host;

        public ProbeFailureSwitch Failures => host.Services.GetRequiredService<ProbeFailureSwitch>();

        public static async Task<Fixture> StartAsync(SerginWebApiFactory<Program> factory, int retryIntervalSeconds = 30)
        {
            string licence = RequiresTickerQLicenseFactAttribute.LicensePath!;
            await TickerQTestStore.ResetSchemaAsync(TickerQTestStore.ConnectionString(factory.Services), licence);

            WebApplicationFactory<Program> host = ScheduledTaskTestHost.Create(factory, builder =>
            {
                builder.UseSetting(TickerQTestStore.LicenseKey, licence);
                builder.UseSetting("Sergin:ScheduledTasks:RetryIntervalsSeconds:0", retryIntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.ConfigureServices((context, services) =>
                {
                    IConfigurationSection sergin = context.Configuration.GetSection("Sergin");
                    services.AddSerginTickerQ(sergin, sergin.GetSection("ConnectionStrings")["Database"]!);
                    services.AddSingleton<ProbeFailureSwitch>();
                    services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
                    services.AddScoped<IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
                });
            });

            var fixture = new Fixture(host);
            await ScheduledTaskTestHost.ResetSchemaAsync(host);
            host.Services.GetRequiredService<IHost>().UseTickerQ();

            fixture.tree = await RbacScenario.BuildAsync(host);
            fixture.manager = await fixture.tree.CreateGrantedCallerAsync(
                fixture.tree.North, ScheduledTaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);

            return fixture;
        }

        public async Task<ErrorOr<T>> SendAsync<T>(IRequest<ErrorOr<T>> request)
        {
            using IServiceScope circuit = host.Services.CreateScope();
            circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = manager;
            circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.North);

            return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(request);
        }

        public async Task<ScheduleProbe> CreateProbeAsync()
        {
            using IServiceScope scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(tree.Feeder1);
            ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

            var probe = ScheduleProbe.Create();
            context.Probes.Add(probe);
            await context.SaveChangesAsync();

            return probe;
        }

        public async Task<IReadOnlyCollection<ScheduledTaskRunItem>> ItemsAsync(Guid runId)
        {
            using IServiceScope scope = host.Services.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>()
                .ScheduledTaskRunItems.AsNoTracking().Where(item => item.RunId == runId).ToListAsync();
        }

        public async Task<T> EventuallyAsync<T>(Func<ScheduledTasksDbContext, Task<T?>> probe, TimeSpan patience)
            where T : class
        {
            DateTime deadline = DateTime.UtcNow + patience;

            while (DateTime.UtcNow < deadline)
            {
                using IServiceScope scope = host.Services.CreateScope();

                if (await probe(scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>()) is { } found)
                {
                    return found;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException($"Nothing matched within {patience}.");
        }

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
