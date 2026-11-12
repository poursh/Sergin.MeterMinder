using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MediatR;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks.Queries;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Times;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The schedules section keeps itself current. A reload the host asks for (after its Schedule dialog closes) re-renders it,
/// and while a run it lists is still Running it reads again on its own timer, so a Run now's outcome shows without a page
/// reload. The timer's interval is the section's public <c>PollInterval</c> parameter, shortened here to 100 ms; the
/// real default is two seconds. The component is rendered in process, as <see cref="ScheduledTaskUiTests"/> renders the dialog.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScheduledTasksSectionRefreshTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";
    private const string NothingScheduled = "Nothing is scheduled for this record.";
    private const string CompletedText = "Completed: 1 of 1 succeeded";

    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;
    private TestCaller delegating = default!;
    private SectionFaults faults = default!;

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                ProbeHandlers.AddTo(services);
                ScheduleProbeUiModule.AddTo(services);
                ProbeUserContextFactory.AddTo(services);
                services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
                services.AddSingleton<SectionFaults>();
                Decorate<ISerginDispatcher>(services, (provider, inner) => new FaultyDispatcher(inner, provider.GetRequiredService<SectionFaults>()));
                Decorate<IUiErrorPresenter>(services, (provider, inner) => new CountingErrorPresenter(inner, provider.GetRequiredService<SectionFaults>()));
            }));

        faults = host.Services.GetRequiredService<SectionFaults>();

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
        // Built before the viewer is current: creating a caller needs the default administrator.
        delegating = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, ScheduleProbePermissions.Touch.Permission);
        users.Current = await tree.CreateGrantedCallerAsync(
            Scope.RootId.Value, ScheduledTaskPermissions.Manage.Permission, ScheduleProbePermissions.Touch.Permission);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task HostInvokedReload_ReRendersTheSection()
    {
        Guid probe = await CreateProbeAsync();

        await using AsyncServiceScope scope = OpenScope(probe);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SectionHost.Section = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<SectionHost>(Parameters(probe, TimeSpan.FromHours(1)));
            Assert.Contains(NothingScheduled, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            await CreateTaskAsync(probe);

            // Nothing re-renders the section on its own: it is the host's call, as the device view makes after Schedule….
            await SectionHost.Section!.ReloadAsync();

            string html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(TouchName, html, StringComparison.Ordinal);
            Assert.DoesNotContain(NothingScheduled, html, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ARunningOneOffRun_IsPolled_UntilItsFinalStatusShows()
    {
        Guid probe = await CreateProbeAsync();
        Guid runId = await StartOneOffRunAsync(probe);

        await using AsyncServiceScope scope = OpenScope(probe);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<SectionHost>(Parameters(probe, TimeSpan.FromMilliseconds(100)));

            Assert.Contains("Running", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            await CompleteRunAsync(runId);

            string html = string.Empty;

            for (int attempt = 0; attempt < 50 && !html.Contains(CompletedText, StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(100);
                html = WebUtility.HtmlDecode(root.ToHtmlString());
            }

            // No ReloadAsync call anywhere above: the section's own timer read the finished run.
            Assert.Contains(CompletedText, html, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task WithNoRunningRun_TheSectionDoesNotPoll()
    {
        Guid probe = await CreateProbeAsync();
        Guid runId = await StartOneOffRunAsync(probe);
        await CompleteRunAsync(runId);

        await using AsyncServiceScope scope = OpenScope(probe);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<SectionHost>(Parameters(probe, TimeSpan.FromMilliseconds(100)));
            Assert.Contains(CompletedText, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            // A task added behind its back is not picked up: no timer is running to find it.
            await CreateTaskAsync(probe);
            await Task.Delay(600);

            Assert.DoesNotContain(TouchName, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task DisposedWhileAReloadAwaits_NoTimerSurvives()
    {
        Guid probe = await CreateProbeAsync();
        await StartOneOffRunAsync(probe);

        await using AsyncServiceScope scope = OpenScope(probe);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SectionHost.Section = null;
            await renderer.RenderComponentAsync<SectionHost>(Parameters(probe, TimeSpan.FromMilliseconds(100)));
            SerginScheduledTasksSection section = SectionHost.Section!;
            Assert.True(section.IsPolling);

            // The component goes away while a reload is still reading; the run is still Running when the read returns.
            section.Dispose();
            Assert.False(section.IsPolling);

            Task reload = section.ReloadAsync();
            await reload;

            Assert.False(section.IsPolling);
        });
    }

    [Fact]
    public async Task AReadErrorWhileARunIsRunning_StopsPolling_AndNotifiesOnce()
    {
        Guid probe = await CreateProbeAsync();
        await StartOneOffRunAsync(probe);

        await using AsyncServiceScope scope = OpenScope(probe);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SectionHost.Section = null;
            await renderer.RenderComponentAsync<SectionHost>(Parameters(probe, TimeSpan.FromMilliseconds(100)));
            SerginScheduledTasksSection section = SectionHost.Section!;
            Assert.True(section.IsPolling);

            // From now on the one-off runs read fails; the run stays Running, so only the error can stop the timer.
            faults.FailOneOffReads = true;

            for (int attempt = 0; attempt < 50 && section.IsPolling; attempt++)
            {
                await Task.Delay(100);
            }

            Assert.False(section.IsPolling);

            // Several intervals later still one notification: no tick repeats it.
            await Task.Delay(600);
            Assert.False(section.IsPolling);
            Assert.Equal(1, faults.Notifications);
        });
    }

    [Fact]
    public async Task ANewTarget_ReloadsTheSection_WithThatTargetsSchedules()
    {
        Guid scheduled = await CreateProbeAsync();
        Guid empty = await CreateProbeAsync();
        await CreateTaskAsync(scheduled);

        await using AsyncServiceScope scope = OpenScope(empty);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SectionHost.Current = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<SectionHost>(Parameters(empty, TimeSpan.FromHours(1)));
            Assert.Contains(NothingScheduled, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            // The same section instance, handed another record, as a detail view reused across a navigation is.
            await SectionHost.Current!.ShowAsync(scheduled);

            string html = WebUtility.HtmlDecode(root.ToHtmlString());

            for (int attempt = 0; attempt < 50 && !html.Contains(TouchName, StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(100);
                html = WebUtility.HtmlDecode(root.ToHtmlString());
            }

            Assert.Contains(TouchName, html, StringComparison.Ordinal);
            Assert.DoesNotContain(NothingScheduled, html, StringComparison.Ordinal);
        });
    }

    // Wraps the registered implementation (internal to SharedKernel) rather than naming it.
    private static void Decorate<TService>(IServiceCollection services, Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        ServiceDescriptor registered = services.Last(descriptor => descriptor.ServiceType == typeof(TService));
        Type implementation = registered.ImplementationType
            ?? throw new InvalidOperationException($"{typeof(TService).Name} is not registered by type.");
        services.Replace(ServiceDescriptor.Describe(
            typeof(TService),
            provider => decorate(provider, (TService)ActivatorUtilities.CreateInstance(provider, implementation)),
            registered.Lifetime));
    }

    private static ParameterView Parameters(Guid probe, TimeSpan interval) => ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(SectionHost.TargetId)] = probe,
        [nameof(SectionHost.Interval)] = interval,
    });

    private AsyncServiceScope OpenScope(Guid probe)
    {
        AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/tst/probes/{probe}");

        return scope;
    }

    private async Task<Guid> CreateProbeAsync()
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        var probe = ScheduleProbe.Create();
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe.Id.Value;
    }

    private async Task CreateTaskAsync(Guid probe)
    {
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));

        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);

        var task = ScheduledTask.Create(TouchName, arguments, [new ScheduledTaskTarget(probe, null)], "15 3 * * *", "UTC", delegating.Id);
        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);
    }

    // A one-off run as the dialog's Run now starts it: Running, until something completes it.
    private async Task<Guid> StartOneOffRunAsync(Guid probe)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr.ErrorOr<ScheduledRunStartedResponse> result = await dispatcher.SendAsync(new RunCommandOnceCommand(
            ScheduledTasksDbContext.Schema,
            new TouchProbeCommand(new ScheduleProbeId(probe), new ProbeNote("note")),
            [new ScheduledTaskTarget(probe, null)]));
        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);

        return result.Value.RunId;
    }

    private async Task CompleteRunAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        ScheduledTaskRun run = await context.ScheduledTaskRuns.FindAsync(runId) ?? throw new InvalidOperationException("run missing");
        run.Complete(1, 0, DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    /// <summary>What the section's reads and notifications see: a switch that fails the one-off runs read, and a count.</summary>
    private sealed class SectionFaults
    {
        private int notifications;
        private volatile bool failOneOffReads;

        public bool FailOneOffReads
        {
            get => failOneOffReads;
            set => failOneOffReads = value;
        }

        public int Notifications => Volatile.Read(ref notifications);

        public void Notified() => Interlocked.Increment(ref notifications);
    }

    private sealed class FaultyDispatcher(ISerginDispatcher inner, SectionFaults faults) : ISerginDispatcher
    {
        public Task<ErrorOr.ErrorOr<TResponse>> SendAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, CancellationToken cancellationToken = default) =>
            faults.FailOneOffReads && request is GetOneOffRunsForTargetQuery
                ? Task.FromResult<ErrorOr.ErrorOr<TResponse>>(ErrorOr.Error.Failure("test.one-off-read-failed"))
                : inner.SendAsync(request, cancellationToken);

        public Task<ErrorOr.ErrorOr<Versioned<TResponse>>> SendVersionedAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default) =>
            inner.SendVersionedAsync(request, expected, cancellationToken);
    }

    private sealed class CountingErrorPresenter(IUiErrorPresenter inner, SectionFaults faults) : IUiErrorPresenter
    {
        public SerginProblem Present(ErrorOr.Error error)
        {
            faults.Notified();
            return inner.Present(error);
        }

        public void Notify(ErrorOr.Error error)
        {
            faults.Notified();
            inner.Notify(error);
        }

        public void Notify(IReadOnlyList<ErrorOr.Error> errors)
        {
            faults.Notified();
            inner.Notify(errors);
        }
    }

    /// <summary>The providers the section's tooltips render into, and a handle on the section for the host-style reload.</summary>
    private sealed class SectionHost : ComponentBase
    {
        private Guid? shown;

        public static SerginScheduledTasksSection? Section { get; set; }

        public static SectionHost? Current { get; set; }

        [Parameter]
        public Guid TargetId { get; set; }

        [Parameter]
        public TimeSpan Interval { get; set; }

        /// <summary>Hands the section another record, as a parent re-rendering with a new parameter does.</summary>
        public Task ShowAsync(Guid target)
        {
            shown = target;
            return InvokeAsync(StateHasChanged);
        }

        protected override void OnInitialized() => Current = this;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<SerginScheduledTasksSection>(1);
            builder.AddComponentParameter(2, nameof(SerginScheduledTasksSection.Schema), ScheduledTasksDbContext.Schema);
            builder.AddComponentParameter(3, nameof(SerginScheduledTasksSection.TargetId), shown ?? TargetId);
            builder.AddComponentParameter(4, nameof(SerginScheduledTasksSection.PollInterval), Interval);
            builder.AddComponentReferenceCapture(5, instance => Section = (SerginScheduledTasksSection)instance);
            builder.CloseComponent();
        }
    }
}
