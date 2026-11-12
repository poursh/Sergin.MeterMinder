using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Localization;
using Sergin.SharedKernel.Application.Localizations;
using ErrorOr;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.ScheduledTasks.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks.Queries;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Times;
using Sergin.Administration.UserAccess.Domain.Scopes;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The scheduled-task UI parts as the prerender draws them on the test-only <c>/tst/probes/{id}</c> page: the section
/// of a record's schedules (hidden without the manage permission), the Schedule action (hidden without the command's
/// permission), the cron picker's pure parts, and the pseudo-locale check of the page — the sweep itself drives the
/// real host, which carries no scheduled-task page, so this covers the new parts under the same rule.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class ScheduledTaskUiTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string TouchName = "tst.probes.touch";
    private const string PseudoCulture = "qps-Ploc";
    private const string Cron = "15 3 * * *";
    private const string EngineOff = "Scheduled runs are off until the scheduling engine is configured. Run now still works.";

    private readonly TestTranslationStore translations = new();

    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;

    public async Task InitializeAsync()
    {
        translations.AddLanguage(PseudoCulture, published: true);

        host = ScheduledTaskTestHost.Create(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                ProbeHandlers.AddTo(services);
                ScheduleProbeUiModule.AddTo(services);
                ProbeUserContextFactory.AddTo(services);
                services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
            });

            builder.ConfigureTestServices(services => services.AddSingleton<ITranslationStore>(translations));
        });

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task Section_Prerenders_TasksForTheRecord()
    {
        Guid probe = await CreateProbeAsync();
        await CreateTaskAsync(probe);
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);

        string html = await GetAsync($"/tst/probes/{probe}");

        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.Contains(Cron, html, StringComparison.Ordinal);
        Assert.Contains(TouchName, html, StringComparison.Ordinal);
        Assert.Contains("Schedule…", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Section_Hidden_WithoutManage()
    {
        Guid probe = await CreateProbeAsync();
        await CreateTaskAsync(probe);
        await GrantAsync(ScheduleProbePermissions.Touch);

        string html = await GetAsync($"/tst/probes/{probe}");

        Assert.DoesNotContain("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Cron, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule…", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleAction_Hidden_WithoutCommandPermission()
    {
        Guid probe = await CreateProbeAsync();
        await GrantAsync(ScheduledTaskPermissions.Manage);

        string html = await GetAsync($"/tst/probes/{probe}");

        // The section is there (Manage is held); the action is not (the command's own permission is not).
        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule…", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_UnderPseudoLocale_HasNoLiteralText()
    {
        Guid probe = await CreateProbeAsync();
        await CreateTaskAsync(probe);
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);

        await ApplyPseudoLocaleAsync();

        string html = await GetAsync($"/tst/probes/{probe}", PseudoCulture);

        // From the page's own body on: the app bar and drawer are the shell's, swept with the real host.
        int marker = html.IndexOf("sergin-aggregate-page", StringComparison.Ordinal);
        Assert.True(marker > 0, "The probe page rendered no view body.");

        int start = html.IndexOf('>', marker) + 1;

        // The task's own values — its command name, cron and zone — are data; so is the next run, a culture-formatted date.
        string[] data = [probe.ToString(), TouchName, Cron, "UTC"];
        string[] leftovers =
        [
            .. PseudoLocaleSweepTests.VisibleTexts("<body>" + html[start..])
                .Where(text => !PseudoLocaleSweepTests.IsTranslatedOrData(text, data) && !FormattedDate().IsMatch(text)),
        ];

        Assert.True(leftovers.Length == 0, "Untranslated text:\n" + string.Join('\n', leftovers));
        Assert.Contains("⟦Scheduled tasks⟧", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleDialog_RendersItsParts_UnderPseudoLocale()
    {
        Guid probe = await CreateProbeAsync();
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);
        await ApplyPseudoLocaleAsync();

        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(PseudoCulture);

        try
        {
            await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
            IDialogService dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();

            // Outside a request nothing has told the navigation manager where it is; the dialog provider listens to it.
            ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
                .Initialize("http://localhost/", $"http://localhost/tst/probes/{probe}");

            await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
            string html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                HtmlRootComponent root = await renderer.RenderComponentAsync<ProbeDialogProviders>();
                DialogParameters<SerginScheduleDialog> parameters = new()
                {
                    { dialog => dialog.Schema, ScheduledTasksDbContext.Schema },
                    { dialog => dialog.CommandTitle, ScheduleProbeUiTexts.CommandTitle },
                    { dialog => dialog.BuildTemplate, () => new TouchProbeCommand(new ScheduleProbeId(probe), new ProbeNote("note")) },
                    { dialog => dialog.Targets, new[] { new ScheduledTaskTarget(probe, null) } },
                    { dialog => dialog.TargetLabels, new[] { probe.ToString() } },
                };

                await dialogs.ShowAsync<SerginScheduleDialog>(string.Empty, parameters);

                // The dialog renders after the provider is told; wait for it, not for a fixed time.
                for (int attempt = 0; attempt < 50 && !root.ToHtmlString().Contains("⟦Run now⟧", StringComparison.Ordinal); attempt++)
                {
                    await Task.Delay(100);
                }

                return WebUtility.HtmlDecode(root.ToHtmlString());
            });

            foreach (string expected in new[] { "⟦Touch the probe⟧", "⟦Run now⟧", "⟦Save⟧", "⟦Records⟧", "⟦Schedule⟧", "⟦Next runs⟧", "⟦Time zone: " })
            {
                Assert.Contains(expected, html, StringComparison.Ordinal);
            }

            string[] data = [probe.ToString(), TimeZoneInfo.Utc.DisplayName, "02:00"];
            string[] leftovers =
            [
                .. PseudoLocaleSweepTests.VisibleTexts("<body>" + html + "</body>")
                    .Where(text => !PseudoLocaleSweepTests.IsTranslatedOrData(text, data) && !FormattedDate().IsMatch(text)),
            ];

            Assert.True(leftovers.Length == 0, "Untranslated text:" + Environment.NewLine + string.Join(Environment.NewLine, leftovers));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CronPicker_DailyAt0200_Produces_0_2_Star() =>
        Assert.Equal("0 2 * * *", SerginCronPicker.ToCron(CronChoice.Daily, new TimeOnly(2, 0)));

    [Fact]
    public void CronPicker_BuildsHourlyAndWeekly()
    {
        Assert.Equal("30 * * * *", SerginCronPicker.ToCron(CronChoice.Hourly, new TimeOnly(9, 30)));
        Assert.Equal("5 14 * * 3", SerginCronPicker.ToCron(CronChoice.Weekly, new TimeOnly(14, 5), DayOfWeek.Wednesday));
        Assert.Equal("*/10 * * * *", SerginCronPicker.ToCron(CronChoice.Custom, new TimeOnly(0, 0), custom: "*/10 * * * *"));
    }

    [Theory]
    [InlineData("0 2 * * *", CronChoice.Daily)]
    [InlineData("30 * * * *", CronChoice.Hourly)]
    [InlineData("5 14 * * 3", CronChoice.Weekly)]
    [InlineData("0 0 1 * *", CronChoice.Custom)]
    [InlineData("0 0 0 * * *", CronChoice.Custom)]
    public void CronPicker_ReadsBackTheChoiceItBuilds(string cron, CronChoice expected) =>
        Assert.Equal(expected, SerginCronPicker.ChoiceOf(cron));

    [Fact]
    public async Task Section_ShowsTheEnginesNextRun_OrNotAvailable()
    {
        Guid probe = await CreateProbeAsync();
        await CreateTaskAsync(probe);
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);

        // The fake engine's next run is 02:00 UTC the next day.
        string withRun = await GetAsync($"/tst/probes/{probe}");
        Assert.Matches(@"\d{1,2} \p{L}+ \d{4} 02:00", withRun);
        Assert.DoesNotContain("Not available", withRun, StringComparison.Ordinal);

        host.Services.GetRequiredService<FakeScheduledTaskEngine>().NoOccurrences = true;
        string without = await GetAsync($"/tst/probes/{probe}");
        Assert.Contains("Not available", without, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleDialog_OnAStaleSave_ReloadsTheVersion_AndKeepsTheDialogOpen()
    {
        Guid probe = await CreateProbeAsync();
        ScheduledTaskId id = await CreateTaskAsync(probe);
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        IDialogService dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/tst/probes/{probe}");

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<ProbeDialogProviders>();
            DialogParameters<SerginScheduleDialog> parameters = new()
            {
                { dialog => dialog.Schema, ScheduledTasksDbContext.Schema },
                { dialog => dialog.CommandTitle, ScheduleProbeUiTexts.CommandTitle },
                { dialog => dialog.BuildTemplate, () => new TouchProbeCommand(new ScheduleProbeId(probe), new ProbeNote("note")) },
                { dialog => dialog.Targets, new[] { new ScheduledTaskTarget(probe, null) } },
                { dialog => dialog.TargetLabels, new[] { probe.ToString() } },
                { dialog => dialog.EditingId, id },
            };

            IDialogReference reference = await dialogs.ShowAsync<SerginScheduleDialog>(string.Empty, parameters);
            var dialog = (SerginScheduleDialog)reference.Dialog!;

            for (int attempt = 0; attempt < 50 && dialog.Version is null; attempt++)
            {
                await Task.Delay(100);
            }

            RowVersion opened = dialog.Version!.Value;

            // Someone else saves the schedule: its version moves on.
            ErrorOr<Versioned<UpdateScheduledTaskCommandResponse>> elsewhere = await UpdateCronAsync(id, "30 4 * * *");
            Assert.False(elsewhere.IsError, elsewhere.IsError ? elsewhere.FirstError.Description : string.Empty);

            // The dialog's save is now stale: it reads the task again instead of keeping its old version.
            await dialog.SaveAsync();

            Assert.NotEqual(opened, dialog.Version);
            Assert.Equal(elsewhere.Value.Version, dialog.Version);
        });
    }

    [Fact]
    public async Task EngineOn_HidesTheOffLine()
    {
        Guid probe = await CreateProbeAsync();
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);

        string html = await GetAsync($"/tst/probes/{probe}");

        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
        Assert.DoesNotContain(EngineOff, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EngineOff_SectionShowsTheOffLine()
    {
        Guid probe = await CreateProbeAsync();
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);
        host.Services.GetRequiredService<FakeScheduledTaskEngine>().FiresTriggers = false;

        string html = await GetAsync($"/tst/probes/{probe}");

        Assert.Contains(EngineOff, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ScheduleDialog_ShowsTheOffLine_OnlyWhenTheEngineFiresNothing(bool firesTriggers, bool expected)
    {
        Guid probe = await CreateProbeAsync();
        await GrantAsync(ScheduledTaskPermissions.Manage, ScheduleProbePermissions.Touch);
        host.Services.GetRequiredService<FakeScheduledTaskEngine>().FiresTriggers = firesTriggers;

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        IDialogService dialogs = scope.ServiceProvider.GetRequiredService<IDialogService>();
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/tst/probes/{probe}");

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<ProbeDialogProviders>();
            DialogParameters<SerginScheduleDialog> parameters = new()
            {
                { dialog => dialog.Schema, ScheduledTasksDbContext.Schema },
                { dialog => dialog.CommandTitle, ScheduleProbeUiTexts.CommandTitle },
                { dialog => dialog.BuildTemplate, () => new TouchProbeCommand(new ScheduleProbeId(probe), new ProbeNote("note")) },
                { dialog => dialog.Targets, new[] { new ScheduledTaskTarget(probe, null) } },
                { dialog => dialog.TargetLabels, new[] { probe.ToString() } },
            };

            await dialogs.ShowAsync<SerginScheduleDialog>(string.Empty, parameters);

            for (int attempt = 0; attempt < 50 && !root.ToHtmlString().Contains("Run now", StringComparison.Ordinal); attempt++)
            {
                await Task.Delay(100);
            }

            return WebUtility.HtmlDecode(root.ToHtmlString());
        });

        Assert.Contains("Run now", html, StringComparison.Ordinal);
        Assert.Equal(expected, html.Contains(EngineOff, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^\d{1,2} \p{L}+ \d{4} \d{2}:\d{2}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormattedDate();

    private async Task ApplyPseudoLocaleAsync()
    {
        foreach (TextCatalogEntry entry in host.Services.GetRequiredService<TextCatalog>().Entries)
        {
            translations.Set(PseudoCulture, entry.Definition.Key.Value, $"⟦{entry.Definition.Default}⟧", entry.Definition.Default);
        }

        // A request loads its culture into the cache; the in-process render has none, so it is loaded here.
        await host.Services.GetRequiredService<ITranslationCache>().EnsureLoadedAsync(PseudoCulture, CancellationToken.None);
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

    private async Task<ErrorOr<Versioned<UpdateScheduledTaskCommandResponse>>> UpdateCronAsync(ScheduledTaskId id, string cron)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<Versioned<ScheduledTaskResponse>> current =
            await dispatcher.SendVersionedAsync(new GetScheduledTaskByIdQuery(ScheduledTasksDbContext.Schema, id));

        return await dispatcher.SendVersionedAsync(
            new UpdateScheduledTaskCommand(
                ScheduledTasksDbContext.Schema,
                id,
                new TouchProbeCommand(new ScheduleProbeId(Guid.Empty), new ProbeNote("hello")),
                current.Value.Value.Targets,
                cron,
                "UTC"),
            current.Value.Version);
    }

    private async Task<ScheduledTaskId> CreateTaskAsync(Guid probe)
    {
        TestCaller delegating = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, ScheduleProbePermissions.Touch.Permission);
        string arguments = ScheduledCommandFactory.ToArguments(new TouchProbeCommand(new(Guid.Empty), new ProbeNote("hello")));

        using IServiceScope scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(Scope.RootId.Value);

        var task = ScheduledTask.Create(TouchName, arguments, [new ScheduledTaskTarget(probe, null)], Cron, "UTC", delegating.Id);
        IScheduledTaskStore store = scope.ServiceProvider.GetRequiredService<IScheduledTaskStoreResolver>().For(ScheduledTasksDbContext.Schema);
        store.AddTask(task);
        await store.SaveAsync(CancellationToken.None);

        return task.Id;
    }

    private async Task GrantAsync(params Sergin.SharedKernel.Domain.Securities.PermissionDefinition[] permissions) =>
        users.Current = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, [.. permissions.Select(permission => permission.Permission)]);

    private async Task<string> GetAsync(string path, string? culture = null)
    {
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (culture is not null)
        {
            request.Headers.Add("Cookie", $".AspNetCore.Culture={Uri.EscapeDataString($"c={culture}|uic={culture}")}");
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}: {body[..Math.Min(body.Length, 3000)]}");

        return body;
    }
}
