using System.Net;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Activity;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The app bar's activity indicator. It is not prerendered: it loads after the circuit's first render. The test drives
/// that first-render start by hand (<c>StartAsync</c>), in process, since the shell tests have no interactive renderer;
/// what the popover looks like in a browser is the headless walk's. It never breaks the shell: a failed load and an
/// anonymous viewer render nothing, and a closing circuit stops listening and polling.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ActivityIndicatorRenderingTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Marker = "sergin-activity";
    private const string Command = "tst.probes.touch";

    private WebApplicationFactory<Program> host = default!;
    private TestCaller caller = default!;

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder =>
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(new FakeScheduledTaskEngine()));
                services.AddSingleton<IndicatorFaults>();
                ServiceDescriptor registered = services.Last(descriptor => descriptor.ServiceType == typeof(ISerginDispatcher));
                services.Replace(ServiceDescriptor.Scoped<ISerginDispatcher>(provider => new FaultyDispatcher(
                    (ISerginDispatcher)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!),
                    provider.GetRequiredService<IndicatorFaults>())));
            }));

        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        caller = new TestCaller();
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ThePrerenderedPage_HasNoIndicator_ItLoadsAfterTheFirstInteractiveRender()
    {
        using HttpClient client = factory.CreateClient();

        string html = await client.GetStringAsync(new Uri("/dm/devices", UriKind.Relative));

        Assert.DoesNotContain(Marker, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoRuns_TheIconShows_WithNoBadge()
    {
        await RenderAsync(caller, async (indicator, html) =>
        {
            Assert.DoesNotContain(Marker, html(), StringComparison.Ordinal);

            await indicator.StartAsync();

            Assert.Contains(Marker, html(), StringComparison.Ordinal);
            Assert.Contains("aria-label=\"Activity, 0 running\"", html(), StringComparison.Ordinal);
            Assert.DoesNotContain("mud-badge ", html(), StringComparison.Ordinal);
            Assert.False(indicator.IsPolling);
        });
    }

    [Fact]
    public async Task WithOneRunningRun_TheBadgeShowsOne_AndItPolls()
    {
        await SeedRunAsync(caller.Id, label: "P-1");

        await RenderAsync(caller, async (indicator, html) =>
        {
            await indicator.StartAsync();

            Assert.Contains("aria-label=\"Activity, 1 running\"", html(), StringComparison.Ordinal);
            Assert.Matches(@"class=""mud-badge [^""]*""[^>]*>\s*1\s*<", html());
            Assert.True(indicator.IsPolling);
            Assert.True(indicator.IsSubscribed);
        });
    }

    [Fact]
    public async Task EverySourceThrowing_RendersTheIconAndNothingElse()
    {
        await SeedRunAsync(caller.Id, label: "P-1");
        await using WebApplicationFactory<Program> broken = host.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundRunSource>();
                services.AddScoped<IBackgroundRunSource, ThrowingBackgroundRunSource>();
            }));

        await RenderAsync(broken, caller, async (indicator, html) =>
        {
            await indicator.StartAsync();

            Assert.Contains("aria-label=\"Activity, 0 running\"", html(), StringComparison.Ordinal);
            Assert.DoesNotContain("mud-badge ", html(), StringComparison.Ordinal);
            Assert.DoesNotContain("mud-alert", html(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AFailedLoad_RendersNothing_NotAnErrorPanel()
    {
        await SeedRunAsync(caller.Id, label: "P-1");
        host.Services.GetRequiredService<IndicatorFaults>().FailActivity = true;

        try
        {
            await RenderAsync(caller, async (indicator, html) =>
            {
                await indicator.StartAsync();

                Assert.DoesNotContain(Marker, html(), StringComparison.Ordinal);
                Assert.DoesNotContain("mud-alert", html(), StringComparison.Ordinal);
                Assert.False(indicator.IsPolling);
            });
        }
        finally
        {
            host.Services.GetRequiredService<IndicatorFaults>().FailActivity = false;
        }
    }

    [Fact]
    public async Task AnAnonymousViewer_GetsNothing_AndIsNotSubscribed()
    {
        await RenderAsync(ClaimsPrincipalUserContext.Anonymous, async (indicator, html) =>
        {
            await indicator.StartAsync();

            Assert.DoesNotContain(Marker, html(), StringComparison.Ordinal);
            Assert.False(indicator.IsSubscribed);
        });
    }

    [Fact]
    public async Task AClosingCircuit_Unsubscribes_AndStopsPolling()
    {
        await SeedRunAsync(caller.Id, label: "P-1");

        await RenderAsync(caller, async (indicator, _) =>
        {
            await indicator.StartAsync();
            Assert.True(indicator.IsSubscribed);
            Assert.True(indicator.IsPolling);

            indicator.Dispose();

            Assert.False(indicator.IsSubscribed);
            Assert.False(indicator.IsPolling);

            // A change published after the circuit closed reaches no handler and starts no reload.
            host.Services.GetRequiredService<IRunNotifier>().Publish(new RunChanged(new BackgroundRunRef("tst", Guid.CreateVersion7()), caller.Id));
            await indicator.ReloadAsync();
            Assert.False(indicator.IsPolling);
        });
    }

    [Fact]
    public async Task ANotifiedChange_ReloadsTheIndicator()
    {
        await RenderAsync(caller, async (indicator, html) =>
        {
            await indicator.StartAsync();
            Assert.Contains("aria-label=\"Activity, 0 running\"", html(), StringComparison.Ordinal);

            Guid run = await SeedRunAsync(caller.Id, label: "P-1");
            host.Services.GetRequiredService<IRunNotifier>().Publish(new RunChanged(new BackgroundRunRef("tst", run), caller.Id));

            for (int attempt = 0; attempt < 50 && !html().Contains("Activity, 1 running", StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(100);
            }

            Assert.Contains("aria-label=\"Activity, 1 running\"", html(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ARunFinishingBetweenTwoLoads_RaisesOneSnackbar_AnAlreadyFinishedOneNone()
    {
        Guid finished = await SeedRunAsync(caller.Id, label: "OLD-1");
        await CompleteAsync(finished);
        Guid running = await SeedRunAsync(caller.Id, label: "P-1");

        await RenderAsync(caller, async (indicator, _, snackbar) =>
        {
            await indicator.StartAsync();
            Assert.Empty(snackbar.ShownSnackbars);

            await CompleteAsync(running);
            await indicator.ReloadAsync();
            await indicator.ReloadAsync();

            Snackbar shown = Assert.Single(snackbar.ShownSnackbars);
            Assert.Equal($"{Command} finished for P-1.", shown.Message);
        });
    }

    [Fact]
    public async Task ABurstOfReloads_IsSingleFlight_AnOlderReadNeverOverwritesANewerOne_AndTheRunIsAnnouncedOnce()
    {
        Guid running = await SeedRunAsync(caller.Id, label: "P-1");
        IndicatorFaults faults = host.Services.GetRequiredService<IndicatorFaults>();

        try
        {
            await RenderAsync(caller, async (indicator, html, snackbar) =>
            {
                await indicator.StartAsync();
                Assert.Contains("aria-label=\"Activity, 1 running\"", html(), StringComparison.Ordinal);

                // The next read answers "Running" and is then held back, while the run finishes.
                var oldAnswerRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int held = 0;
                faults.AfterRead = async () =>
                {
                    if (Interlocked.Exchange(ref held, 1) == 0)
                    {
                        oldAnswerRead.SetResult();
                        await Task.Delay(500);
                    }
                };
                int readsBefore = faults.Reads;

                Task first = indicator.ReloadAsync();
                await oldAnswerRead.Task;
                await CompleteAsync(running);

                // A burst, as a bulk run's item saves publish one change each, while the old read is still out.
                IRunNotifier notifier = host.Services.GetRequiredService<IRunNotifier>();
                for (int change = 0; change < 5; change++)
                {
                    notifier.Publish(new RunChanged(new BackgroundRunRef("tst", running), caller.Id));
                }

                Task second = indicator.ReloadAsync();
                await Task.WhenAll(first, second);

                // Settled: no read out and no notifier-driven reload waiting for its interval, so none is left to come back
                // late with the older answer. Bounded: the trailing reload is due within a second.
                for (int attempt = 0; attempt < 50 && (!indicator.IsIdle || html().Contains("mud-badge ", StringComparison.Ordinal)); attempt++)
                {
                    await Task.Delay(100);
                }

                Assert.True(indicator.IsIdle);
                Assert.Contains("aria-label=\"Activity, 0 running\"", html(), StringComparison.Ordinal);
                Assert.False(indicator.IsPolling);
                Assert.Equal($"{Command} finished for P-1.", Assert.Single(snackbar.ShownSnackbars).Message);
                // The held read, one more for the explicit reload and the first change together, and at most one trailing
                // read for the rest of the burst (rate-limited to one a second): coalesced, not one read per change.
                Assert.InRange(faults.Reads - readsBefore, 2, 3);
            });
        }
        finally
        {
            faults.AfterRead = null;
        }
    }

    [Fact]
    public async Task ARunThatStartsAndFinishesBetweenTwoLoads_IsStillAnnounced()
    {
        await RenderAsync(caller, async (indicator, html, snackbar) =>
        {
            await indicator.StartAsync();
            Assert.Contains("aria-label=\"Activity, 0 running\"", html(), StringComparison.Ordinal);

            // Never seen running: it started after the first load and finished before the next one.
            Guid fast = await SeedRunAsync(caller.Id, label: "FAST-1");
            await CompleteAsync(fast);
            await indicator.ReloadAsync();
            await indicator.ReloadAsync();

            Assert.Equal($"{Command} finished for FAST-1.", Assert.Single(snackbar.ShownSnackbars).Message);
        });
    }

    [Fact]
    public async Task AReadThatIsCancelled_RendersNothing_AndDoesNotThrow()
    {
        IndicatorFaults faults = host.Services.GetRequiredService<IndicatorFaults>();
        faults.ThrowOnRead = new TaskCanceledException("interop timed out");

        try
        {
            await RenderAsync(caller, async (indicator, html) =>
            {
                await indicator.StartAsync();

                Assert.DoesNotContain(Marker, html(), StringComparison.Ordinal);
                Assert.False(indicator.IsPolling);
            });
        }
        finally
        {
            faults.ThrowOnRead = null;
        }
    }

    [Fact]
    public async Task NotifiedChanges_AreRateLimited_ToOneReloadASecond_WithOneTrailingReload()
    {
        IndicatorFaults faults = host.Services.GetRequiredService<IndicatorFaults>();

        await RenderAsync(caller, async (indicator, html) =>
        {
            await indicator.StartAsync();
            int readsBefore = faults.Reads;
            IRunNotifier notifier = host.Services.GetRequiredService<IRunNotifier>();
            Guid run = await SeedRunAsync(caller.Id, label: "P-1");

            for (int change = 0; change < 10; change++)
            {
                notifier.Publish(new RunChanged(new BackgroundRunRef("tst", run), caller.Id));
                await Task.Delay(20);
            }

            for (int attempt = 0; attempt < 50 && (faults.Reads - readsBefore < 2 || !indicator.IsIdle); attempt++)
            {
                await Task.Delay(100);
            }

            // One read at once for the first change, one trailing read for the other nine: the last change is not lost.
            Assert.Equal(2, faults.Reads - readsBefore);
            Assert.Contains("aria-label=\"Activity, 1 running\"", html(), StringComparison.Ordinal);
        });
    }

    private Task RenderAsync(IUserContext user, Func<SerginActivityIndicator, Func<string>, Task> act) =>
        RenderAsync(host, user, (indicator, html, _) => act(indicator, html));

    private static Task RenderAsync(WebApplicationFactory<Program> on, IUserContext user, Func<SerginActivityIndicator, Func<string>, Task> act) =>
        RenderAsync(on, user, (indicator, html, _) => act(indicator, html));

    private Task RenderAsync(IUserContext user, Func<SerginActivityIndicator, Func<string>, ISnackbar, Task> act) =>
        RenderAsync(host, user, act);

    private static async Task RenderAsync(
        WebApplicationFactory<Program> on, IUserContext user, Func<SerginActivityIndicator, Func<string>, ISnackbar, Task> act)
    {
        await using AsyncServiceScope scope = on.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = user;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", "http://localhost/dm/devices");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            IndicatorHost.Indicator = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<IndicatorHost>();
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

            await act(IndicatorHost.Indicator!, Html, scope.ServiceProvider.GetRequiredService<ISnackbar>());
        });
    }

    private async Task<Guid> SeedRunAsync(UserId user, string? label)
    {
        using IServiceScope scope = host.Services.CreateScope();
        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        var definition = new ScheduledRunDefinition(null, ScheduledRunTrigger.Background, user, Guid.CreateVersion7(), "root", Command, "{}");
        DateTime now = DateTime.UtcNow;

        var run = ScheduledTaskRun.Start(definition, 1, now);
        store.AddRun(run, [ScheduledTaskRunItem.Create(run.Id, Guid.CreateVersion7(), null, now, label)]);
        await store.SaveAsync(CancellationToken.None);

        return run.Id;
    }

    private async Task CompleteAsync(Guid runId)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        ScheduledTaskRun run = await context.ScheduledTaskRuns.SingleAsync(found => found.Id == runId);
        foreach (ScheduledTaskRunItem item in await context.ScheduledTaskRunItems.Where(found => found.RunId == runId).ToListAsync())
        {
            item.MarkSucceeded(DateTime.UtcNow);
        }

        run.Complete(1, 0, 0, DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// What the activity reads see: a switch that fails them, as a broken dispatch would, a count, and a hook run after a
    /// read has its answer and before it is returned (to hold an old answer back while a newer read goes ahead).
    /// </summary>
    private sealed class IndicatorFaults
    {
        private volatile bool failActivity;
        private int reads;

        public bool FailActivity
        {
            get => failActivity;
            set => failActivity = value;
        }

        public int Reads => Volatile.Read(ref reads);

        public Func<Task>? AfterRead { get; set; }

        public Exception? ThrowOnRead { get; set; }

        public void Read() => Interlocked.Increment(ref reads);
    }

    private sealed class FaultyDispatcher(ISerginDispatcher inner, IndicatorFaults faults) : ISerginDispatcher
    {
        public async Task<ErrorOr.ErrorOr<TResponse>> SendAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, CancellationToken cancellationToken = default)
        {
            if (request is not GetMyActivityQuery)
            {
                return await inner.SendAsync(request, cancellationToken);
            }

            faults.Read();

            if (faults.ThrowOnRead is { } thrown)
            {
                throw thrown;
            }

            if (faults.FailActivity)
            {
                return ErrorOr.Error.Failure("test.activity-read-failed");
            }

            ErrorOr.ErrorOr<TResponse> answer = await inner.SendAsync(request, cancellationToken);

            if (faults.AfterRead is { } after)
            {
                await after();
            }

            return answer;
        }

        public Task<ErrorOr.ErrorOr<Versioned<TResponse>>> SendVersionedAsync<TResponse>(
            IRequest<ErrorOr.ErrorOr<TResponse>> request, RowVersion? expected = null, CancellationToken cancellationToken = default) =>
            inner.SendVersionedAsync(request, expected, cancellationToken);
    }

    /// <summary>The popover provider the indicator renders into, and a handle on the indicator.</summary>
    private sealed class IndicatorHost : ComponentBase
    {
        public static SerginActivityIndicator? Indicator { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<SerginActivityIndicator>(1);
            builder.AddComponentReferenceCapture(2, instance => Indicator = (SerginActivityIndicator)instance);
            builder.CloseComponent();
        }
    }
}
