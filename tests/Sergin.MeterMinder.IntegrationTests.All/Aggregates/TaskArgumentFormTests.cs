using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskArgumentProbes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.Localization;
using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Application.Tasks.Commands;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Infrastracture.Data;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Actions;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Tasks;
using Sergin.SharedKernel.Presentation.Blazor.TimeZones;
using Sergin.SharedKernel.Presentation.Blazor.Views;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// A task's arguments are asked for in the edit mode its view configuration names, through the generated form machinery:
/// Dialog (the default) opens a modal form from the action; Page links to a routed form page whose trail ends with the
/// record and the task, Start leaving to the record; Inline renders a section of the detail with no action, keeping its
/// values after Start and showing the spinner while the record's run is going. A task with no shown argument starts on
/// click. A bulk start asks once, and the run's one template carries the values for every selected record; a selected
/// record out of the caller's reach starts nothing and is named in the refusal.
/// </summary>
/// <remarks>
/// MudBlazor's dialog content does not render in the in-process renderer, so a dialog is checked by its instance and
/// parameters and its body (<see cref="GeneratedTaskForm"/>) is rendered on its own, as the dialog would hold it.
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class TaskArgumentFormTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string Spinner = "sergin-action-busy";
    private const string WindowName = "tst.probes.window";
    private const string KickName = "tst.probes.kick";
    private const string PseudoCulture = "qps-Ploc";

    private readonly TestTranslationStore translations = new();

    private WebApplicationFactory<Program>? host;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }

    // ---- Dialog ----

    [Fact]
    public async Task Dialog_OpensForm_ValidatesAndStarts()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe one");
        await GrantAsync(Scope.RootId.Value);
        CommandView window = CommandOf(typeof(ProbeWindowTaskCommand));

        Assert.Equal([nameof(ProbeWindowTaskCommand.Note), nameof(ProbeWindowTaskCommand.Hours)], window.TaskFields.Select(field => field.Name));

        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>($"/tst/probes/{probe}", Id(probe), Scope.RootId.Value, async (_, view, _) =>
        {
            var detail = (SerginGeneratedDetailView<ScheduleProbe>)view;
            SerginPageAction action = ActionOf(detail.Actions, ScheduleProbeTexts.Window);
            Assert.Null(action.Href);
            Assert.True(action.OnClick.HasDelegate);

            // The dialog's content does not render in process: its parameters are read off the instance.
            IDialogReference opened = await detail.AskTaskArgumentsAsync(window);
            TaskFormDialog dialog = Assert.IsType<TaskFormDialog>(opened.Dialog);
            Assert.Same(window, dialog.Command);
            Assert.Equal([new TaskTarget(probe, "Probe one")], dialog.Targets);
        });

        await RenderFormAsync(window, [new TaskTarget(probe, "Probe one")], Scope.RootId.Value, async (root, form, _) =>
        {
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());

            Assert.Contains("Note", Html(), StringComparison.Ordinal);
            Assert.Contains("Hours", Html(), StringComparison.Ordinal);

            form.SetValue(nameof(ProbeWindowTaskCommand.Note), "night");
            form.SetValue(nameof(ProbeWindowTaskCommand.Hours), 0m);
            await form.SubmitAsync();

            Assert.Contains("must be between 1 and 24", Html(), StringComparison.Ordinal);
            Assert.Matches(StartDisabled(), Html());
            Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM tst.task_runs WHERE command_name = '{WindowName}'"));

            form.SetValue(nameof(ProbeWindowTaskCommand.Hours), 5m);
            await form.SubmitAsync();

            Assert.DoesNotContain("must be between 1 and 24", Html(), StringComparison.Ordinal);
        });

        (string arguments, IReadOnlyList<Guid?> targets) = Assert.Single(await RunsAsync(WindowName));
        Assert.Contains("night", arguments, StringComparison.Ordinal);
        Assert.Matches(HoursFive(), arguments);
        Assert.Equal([probe], targets);
    }

    [Fact]
    public async Task NoShownArguments_StartsOnClick()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe kicked");
        await GrantAsync(Scope.RootId.Value);

        Assert.Empty(CommandOf(typeof(ProbeKickTaskCommand)).TaskFields);

        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>($"/tst/probes/{probe}", Id(probe), Scope.RootId.Value, async (_, view, _) =>
            await ActionOf(((SerginGeneratedDetailView<ScheduleProbe>)view).Actions, ScheduleProbeTexts.Kick).OnClick.InvokeAsync());

        // Started at once, its NotShown argument at the default the command declares.
        (string arguments, IReadOnlyList<Guid?> targets) = Assert.Single(await RunsAsync(KickName));
        Assert.Matches(TimesThree(), arguments);
        Assert.Equal([probe], targets);
    }

    // ---- Page ----

    [Fact]
    public async Task Page_NavigatesToFormPage_StartsAndReturns()
    {
        await StartHostAsync(typeof(PageScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe paged");
        await GrantAsync(Scope.RootId.Value);
        string detail = $"/tst/probes/{probe}";

        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>(detail, Id(probe), Scope.RootId.Value, (_, view, _) =>
        {
            Assert.Equal($"{detail}/window", ActionOf(((SerginGeneratedDetailView<ScheduleProbe>)view).Actions, ScheduleProbeTexts.Window).Href);
            return Task.CompletedTask;
        });

        // The routed page: the trail ends with the record, linking to it, then the task; Cancel leaves to the record.
        string html = await GetAsync($"{detail}/window");
        int record = html.IndexOf($"href=\"{detail}\"", StringComparison.Ordinal);
        int name = html.IndexOf("Probe paged", StringComparison.Ordinal);
        int label = html.IndexOf("Probe window", name + 1, StringComparison.Ordinal);
        Assert.True(record >= 0 && name > record && label > name, "The trail does not end with the record, then the task.");
        Assert.Contains("Note", html, StringComparison.Ordinal);
        Assert.Matches(CancelTo(detail), html);
        Assert.Equal(0L, await CountAsync($"SELECT count(*) FROM tst.task_runs WHERE command_name = '{WindowName}'"));

        string? left = null;
        SerginViewHost page = new(SerginViewHostKind.Page, href =>
        {
            left = href;
            return Task.CompletedTask;
        });

        await RenderAsync<SerginGeneratedTaskFormView<ScheduleProbe, ProbeWindowTaskCommand>>(
            $"{detail}/window", Id(probe), Scope.RootId.Value, async (_, view, _) =>
            {
                var form = (SerginGeneratedTaskFormView<ScheduleProbe, ProbeWindowTaskCommand>)view;
                form.Arguments.Values.Set(nameof(ProbeWindowTaskCommand.Note), "paged");
                form.Arguments.Values.Set(nameof(ProbeWindowTaskCommand.Hours), 3m);

                await form.SubmitAsync();
            },
            page);

        Assert.Equal(detail, left);
        (string arguments, IReadOnlyList<Guid?> targets) = Assert.Single(await RunsAsync(WindowName));
        Assert.Contains("paged", arguments, StringComparison.Ordinal);
        Assert.Equal([probe], targets);
    }

    [Fact]
    public async Task Page_UnderPseudoLocale_HasNoLiteralText()
    {
        await StartHostAsync(typeof(PageScheduleProbeViewConfiguration), pseudoLocale: true);
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe pseudo");
        await GrantAsync(Scope.RootId.Value);

        foreach (TextCatalogEntry entry in host!.Services.GetRequiredService<TextCatalog>().Entries)
        {
            translations.Set(PseudoCulture, entry.Definition.Key.Value, $"⟦{entry.Definition.Default}⟧", entry.Definition.Default);
        }

        string html = await GetAsync($"/tst/probes/{probe}/window", PseudoCulture);

        // From the page's own header on: the app bar and drawer are the shell's, swept with the real host.
        int marker = html.IndexOf("Probe pseudo", StringComparison.Ordinal);
        Assert.True(marker > 0, "The window page rendered no trail.");

        int start = html.LastIndexOf("<nav", marker, StringComparison.Ordinal);
        string[] data = [probe.ToString(), "Probe pseudo"];
        string[] leftovers =
        [
            .. PseudoLocaleSweepTests.VisibleTexts("<body>" + html[Math.Max(start, 0)..])
                .Where(text => !PseudoLocaleSweepTests.IsTranslatedOrData(text, data)),
        ];

        Assert.True(leftovers.Length == 0, "Untranslated text:\n" + string.Join('\n', leftovers));
        Assert.Contains("⟦Probe window⟧", html, StringComparison.Ordinal);
        Assert.Contains("⟦Start⟧", html, StringComparison.Ordinal);
    }

    // ---- Inline ----

    [Fact]
    public async Task Inline_RendersSection_KeepsValuesAfterStart()
    {
        await StartHostAsync(typeof(InlineScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe inline");
        await GrantAsync(Scope.RootId.Value);
        CommandView window = CommandOf(typeof(ProbeWindowTaskCommand));

        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>($"/tst/probes/{probe}", Id(probe), Scope.RootId.Value, (root, view, services) =>
        {
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
            ActivityPanelState activity = services.GetRequiredService<ActivityPanelState>();

            // A section titled with the task's label, holding its arguments and Start; no action-bar button.
            Assert.Matches(SectionTitled("Probe window"), Html());
            Assert.Contains("Hours", Html(), StringComparison.Ordinal);
            Assert.Contains(">Start<", Html(), StringComparison.Ordinal);
            IReadOnlyList<SerginPageAction> actions = ((SerginGeneratedDetailView<ScheduleProbe>)view).Actions;
            Assert.DoesNotContain(actions, action => action.Label == ScheduleProbeTexts.Window);
            Assert.Contains(actions, action => action.Label == ScheduleProbeTexts.Kick);

            // The spinner follows this record's run of the task: not another record's, nor a finished one.
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
            activity.SetRows([Row(WindowName, Guid.CreateVersion7(), ActivityState.Running), Row(WindowName, probe, ActivityState.Succeeded)]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
            activity.SetRows([Row(WindowName, probe, ActivityState.Running)]);
            Assert.Contains(Spinner, Html(), StringComparison.Ordinal);
            activity.SetRows([]);
            Assert.DoesNotContain(Spinner, Html(), StringComparison.Ordinal);
            return Task.CompletedTask;
        });

        await RenderFormAsync(window, [new TaskTarget(probe, "Probe inline")], Scope.RootId.Value, async (root, form, _) =>
        {
            form.SetValue(nameof(ProbeWindowTaskCommand.Note), "kept");
            form.SetValue(nameof(ProbeWindowTaskCommand.Hours), 2m);
            await form.SubmitAsync();

            // Started, and the form still holds what was entered.
            Assert.Equal("kept", form.Arguments.Values.ValueOf(nameof(ProbeWindowTaskCommand.Note)));
            Assert.Equal(2m, form.Arguments.Values.ValueOf(nameof(ProbeWindowTaskCommand.Hours)));
            Assert.Contains("value=\"kept\"", root.ToHtmlString(), StringComparison.Ordinal);
        });

        Assert.Single(await RunsAsync(WindowName));
    }

    [Fact]
    public async Task InlineSchedulable_SectionsOfferScheduleEntries()
    {
        await StartHostAsync(typeof(InlineScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe scheduled inline");
        await GrantAsync(Scope.RootId.Value, TaskPermissions.Manage);

        // On the detail: the schedulable task's section carries Schedule… and View schedules; the other one only View schedules
        // (a schedule saved before it lost Schedulable() must stay reachable).
        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>($"/tst/probes/{probe}", Id(probe), Scope.RootId.Value, (root, view, _) =>
        {
            string html = WebUtility.HtmlDecode(root.ToHtmlString());
            string sweep = SectionOf(html, "Sweep probe");
            string window = SectionOf(html, "Probe window");

            Assert.Contains("Schedule…", sweep, StringComparison.Ordinal);
            Assert.Contains("View schedules", sweep, StringComparison.Ordinal);
            Assert.DoesNotContain("Schedule…", window, StringComparison.Ordinal);
            Assert.Contains("View schedules", window, StringComparison.Ordinal);
            Assert.DoesNotContain(((SerginGeneratedDetailView<ScheduleProbe>)view).Actions, action => action.Label == ScheduleProbeTexts.Sweep);
            return Task.CompletedTask;
        });

        // On the list: the schedulable collection task's section carries Schedule… and Schedules, and is no header action.
        await RenderAsync<SerginGeneratedListView<ScheduleProbe>>("/tst/probes", [], Scope.RootId.Value, (root, view, _) =>
        {
            string refresh = SectionOf(WebUtility.HtmlDecode(root.ToHtmlString()), "Refresh probes");

            Assert.Contains("Schedule…", refresh, StringComparison.Ordinal);
            Assert.Matches(ButtonText("Schedules"), refresh);
            Assert.DoesNotContain(((SerginGeneratedListView<ScheduleProbe>)view).Actions, action => action.Label == ScheduleProbeTexts.Refresh);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task InlineNotSchedulable_SectionOffersViewSchedulesOnly()
    {
        // The Inline configuration's Window is not Schedulable(); with Manage held its section offers no Schedule…, only View
        // schedules.
        await StartHostAsync(typeof(InlineScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe plain inline");
        await GrantAsync(Scope.RootId.Value, TaskPermissions.Manage);

        await RenderAsync<SerginGeneratedDetailView<ScheduleProbe>>($"/tst/probes/{probe}", Id(probe), Scope.RootId.Value, (root, _, _) =>
        {
            string window = SectionOf(WebUtility.HtmlDecode(root.ToHtmlString()), "Probe window");

            Assert.Contains(">Start<", window, StringComparison.Ordinal);
            Assert.DoesNotContain("Schedule…", window, StringComparison.Ordinal);
            Assert.Contains("View schedules", window, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    // ---- Declared defaults ----

    [Fact]
    public async Task DefaultedArgument_RendersPrefilled_AndStartsWithIt()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        Guid probe = await CreateProbeAsync(Scope.RootId.Value, "Probe swept");
        await GrantAsync(Scope.RootId.Value);
        CommandView sweep = CommandOf(typeof(ProbeSweepTaskCommand));

        Assert.Equal(2, Assert.Single(sweep.TaskFields, field => field.Name == nameof(ProbeSweepTaskCommand.Passes)).Default);

        await RenderFormAsync(sweep, [new TaskTarget(probe, "Probe swept")], Scope.RootId.Value, async (root, form, _) =>
        {
            Assert.Matches(InputValue("2"), root.ToHtmlString());

            form.SetValue(nameof(ProbeSweepTaskCommand.Note), "swept");
            await form.SubmitAsync();
        });

        (string arguments, _) = Assert.Single(await RunsAsync("tst.probes.sweep"));
        Assert.Matches(PassesTwo(), arguments);
    }

    [Fact]
    public async Task CreateForm_OptionalArgument_StartsFromItsDefault()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        await GrantAsync(Scope.RootId.Value);
        string? left = null;
        SerginViewHost page = new(SerginViewHostKind.Page, href =>
        {
            left = href;
            return Task.CompletedTask;
        });

        await RenderAsync<SerginGeneratedFormView<ScheduleProbe>>(
            "/tst/probes/new",
            [],
            Scope.RootId.Value,
            async (root, view, _) =>
            {
                Assert.Matches(InputValue("2"), root.ToHtmlString());

                await ((SerginGeneratedFormView<ScheduleProbe>)view).SubmitAsync();
            },
            page);

        Assert.NotNull(left);
        Assert.Equal(2, host!.Services.GetRequiredService<CreatedProbeRecorder>().Last?.Copies);
    }

    // ---- Bulk ----

    [Fact]
    public async Task Bulk_OneFormForAllSelected()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        Guid[] probes =
        [
            await CreateProbeAsync(Scope.RootId.Value, "Bulk one"),
            await CreateProbeAsync(Scope.RootId.Value, "Bulk two"),
            await CreateProbeAsync(Scope.RootId.Value, "Bulk three"),
        ];
        await GrantAsync(Scope.RootId.Value);
        CommandView window = CommandOf(typeof(ProbeWindowTaskCommand));
        AggregateViewModel model = host!.Services.GetRequiredService<AggregateViewRegistry>().For<ScheduleProbe>();
        IReadOnlyList<TaskTarget> targets = [];

        Assert.True(GeneratedLists.IsBulkAction(window));

        await RenderAsync<GeneratedListBody<ScheduleProbeListItem>>(
            "/tst/probes",
            new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(GeneratedListBody<ScheduleProbeListItem>.Model)] = model },
            Scope.RootId.Value,
            async (_, view, _) =>
            {
                ScheduleProbeListItem[] selected =
                    [new(probes[0], "Bulk one"), new(probes[1], "Bulk two"), new(probes[2], "Bulk three")];

                // One dialog for every selected row, each row a target named as the list shows it.
                IDialogReference opened = await ((GeneratedListBody<ScheduleProbeListItem>)view).AskTaskArgumentsAsync(window, selected);
                TaskFormDialog dialog = Assert.IsType<TaskFormDialog>(opened.Dialog);
                targets = [.. dialog.Targets];
            });

        Assert.Equal(probes.Select(probe => (Guid?)probe), targets.Select(target => target.TargetId));
        Assert.Equal(["Bulk one", "Bulk two", "Bulk three"], targets.Select(target => target.Label));

        await RenderFormAsync(window, targets, Scope.RootId.Value, async (_, form, _) =>
        {
            form.SetValue(nameof(ProbeWindowTaskCommand.Note), "all");
            form.SetValue(nameof(ProbeWindowTaskCommand.Hours), 7m);
            await form.SubmitAsync();
        });

        // One run, its arguments stored once, one item per selected record.
        (string arguments, IReadOnlyList<Guid?> items) = Assert.Single(await RunsAsync(WindowName));
        Assert.Contains("all", arguments, StringComparison.Ordinal);
        Assert.Matches(HoursSeven(), arguments);
        Assert.Equal(probes.Select(probe => (Guid?)probe).Order(), items.Order());
    }

    [Fact]
    public async Task Bulk_OneRecordFailsValidation_StartsNothing()
    {
        await StartHostAsync(typeof(DialogScheduleProbeViewConfiguration));
        Guid northOne = await CreateProbeAsync(tree.North, "North one");
        Guid northTwo = await CreateProbeAsync(tree.North, "North two");
        Guid south = await CreateProbeAsync(tree.South, "South probe");

        // The caller's grants reach North only, which is the current scope: the South probe is out of reach.
        await GrantAsync(tree.North);
        CommandView window = CommandOf(typeof(ProbeWindowTaskCommand));

        await RenderFormAsync(
            window,
            [new TaskTarget(northOne, "North one"), new TaskTarget(northTwo, "North two"), new TaskTarget(south, "South probe")],
            tree.North,
            async (_, form, services) =>
            {
                form.SetValue(nameof(ProbeWindowTaskCommand.Note), "mixed");
                form.SetValue(nameof(ProbeWindowTaskCommand.Hours), 4m);
                await form.SubmitAsync();

                Snackbar refused = Assert.Single(services.GetRequiredService<ISnackbar>().ShownSnackbars);
                Assert.NotEqual(Severity.Info, refused.Severity);
                Assert.StartsWith("South probe: ", refused.Message, StringComparison.Ordinal);
            });

        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM tst.task_runs"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM tst.task_run_items"));
    }

    // ---- Helpers ----

    [GeneratedRegex(@"<button[^>]*disabled[^>]*>\s*(<[^>]*>\s*)*Start")]
    private static partial Regex StartDisabled();

    [GeneratedRegex(@"""hours""\s*:\s*5\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursFive();

    [GeneratedRegex(@"""hours""\s*:\s*7\b", RegexOptions.IgnoreCase)]
    private static partial Regex HoursSeven();

    [GeneratedRegex(@"""times""\s*:\s*3\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimesThree();

    [GeneratedRegex(@"""passes""\s*:\s*2\b", RegexOptions.IgnoreCase)]
    private static partial Regex PassesTwo();

    private static Regex InputValue(string value) =>
        new($"<input[^>]*value=\"{Regex.Escape(value)}\"", RegexOptions.None, TimeSpan.FromSeconds(1));

    // The section titled `title`, from its opening tag to its close.
    private static string SectionOf(string html, string title)
    {
        int start = html.IndexOf($"aria-label=\"{title}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No section titled {title}.");

        return html[start..html.IndexOf("</section>", start, StringComparison.Ordinal)];
    }

    private static Regex CancelTo(string href) => new($"<a[^>]*href=\"{Regex.Escape(href)}\"[^>]*>\\s*(<[^>]*>\\s*)*Cancel", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Regex ButtonText(string text) =>
        new($"<button[^>]*>\\s*(<[^>]*>\\s*)*{Regex.Escape(text)}\\s*<", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Regex SectionTitled(string title) =>
        new($"<section[^>]*aria-label=\"{Regex.Escape(title)}\"", RegexOptions.None, TimeSpan.FromSeconds(1));

    private async Task StartHostAsync(Type viewConfiguration, bool pseudoLocale = false)
    {
        if (pseudoLocale)
        {
            translations.AddLanguage(PseudoCulture, published: true);
        }

        host = TaskTestHost.Create(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                ProbeHandlers.AddTo(services);
                ScheduleProbeUiModule.AddTo(services);
                ProbeUserContextFactory.AddTo(services);
                ScheduleProbeViewHost.AddTo(services, viewConfiguration);
                services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
            });

            if (pseudoLocale)
            {
                builder.ConfigureTestServices(services => services.AddSingleton<ITranslationStore>(translations));
            }
        });

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        await TaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    private CommandView CommandOf(Type request) =>
        Assert.Single(host!.Services.GetRequiredService<AggregateViewRegistry>().For<ScheduleProbe>().Commands, command => command.Request == request);

    private static Dictionary<string, object?> Id(Guid id) => new(StringComparer.Ordinal) { ["Id"] = id };

    private static SerginPageAction ActionOf(IReadOnlyList<SerginPageAction> actions, TextDefinition label) =>
        Assert.Single(actions, action => action.Label == label);

    private static ActivityRow Row(string commandName, Guid target, ActivityState state) =>
        new(new TaskRunRef("tst", Guid.CreateVersion7()), null, commandName, "Probe", null, state, DateTime.UtcNow, null, 0, 1, null, null, target, null, null, 0);

    private async Task<Guid> CreateProbeAsync(Guid scopeId, string name)
    {
        using IServiceScope scope = host!.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        TasksDbContext context = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var probe = ScheduleProbe.Create();
        probe.Touch(new ProbeNote(name));
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        return probe.Id.Value;
    }

    private async Task GrantAsync(Guid scopeId, params PermissionDefinition[] more)
    {
        users.Current = null;
        users.Current = await tree.CreateGrantedCallerAsync(
            scopeId,
            [ScheduleProbePermissions.Read.Permission, ScheduleProbePermissions.Touch.Permission, .. more.Select(permission => permission.Permission)]);
    }

    private async Task RenderAsync<TView>(
        string path,
        Dictionary<string, object?> parameters,
        Guid scopeId,
        Func<HtmlRootComponent, IComponent, IServiceProvider, Task> test,
        SerginViewHost? viewHost = null)
        where TView : IComponent
    {
        await using AsyncServiceScope scope = host!.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        scope.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost{path}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            IComponent? view = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<HostedView<TView>>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(HostedView<TView>.ViewParameters)] = parameters,
                    [nameof(HostedView<TView>.Host)] = viewHost ?? SerginViewHost.Unhosted,
                    [nameof(HostedView<TView>.Capture)] = (Action<IComponent>)(instance => view = instance),
                }));

            await test(root, view!, scope.ServiceProvider);
        });
    }

    private Task RenderFormAsync(
        CommandView command, IReadOnlyCollection<TaskTarget> targets, Guid scopeId, Func<HtmlRootComponent, GeneratedTaskForm, IServiceProvider, Task> test) =>
        RenderAsync<GeneratedTaskForm>(
            "/tst/probes",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(GeneratedTaskForm.Command)] = command,
                [nameof(GeneratedTaskForm.Targets)] = targets,
            },
            scopeId,
            (root, view, services) => test(root, (GeneratedTaskForm)view, services));

    // Each run of the task: its stored arguments and its items' targets.
    private async Task<IReadOnlyList<(string Arguments, IReadOnlyList<Guid?> Targets)>> RunsAsync(string taskName)
    {
        using IServiceScope scope = host!.Services.CreateScope();
        using DbConnection connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.arguments, i.target_id
            FROM tst.task_runs r JOIN tst.task_run_items i ON i.run_id = r.id
            WHERE r.command_name = @name
            ORDER BY r.id
            """;
        DbParameter name = command.CreateParameter();
        name.ParameterName = "name";
        name.Value = taskName;
        command.Parameters.Add(name);

        Dictionary<Guid, (string Arguments, List<Guid?> Targets)> runs = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            Guid run = reader.GetGuid(0);

            if (!runs.TryGetValue(run, out (string Arguments, List<Guid?> Targets) found))
            {
                found = (reader.GetString(1), []);
                runs[run] = found;
            }

            found.Targets.Add(await reader.IsDBNullAsync(2) ? null : reader.GetGuid(2));
        }

        return [.. runs.Values.Select(run => (run.Arguments, (IReadOnlyList<Guid?>)run.Targets))];
    }

    private async Task<long> CountAsync(string sql) => (long)(await GeneratedTaskViewTests.ScalarAsync(host!, sql))!;

    private async Task<string> GetAsync(string path, string? culture = null)
    {
        using HttpClient client = host!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
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

    /// <summary>A view with the providers its menus and dialogs render into, under <see cref="Host"/>; hands the test the view.</summary>
    private sealed class HostedView<TView> : ComponentBase
        where TView : IComponent
    {
        [Parameter]
        public Dictionary<string, object?> ViewParameters { get; set; } = default!;

        [Parameter]
        public SerginViewHost Host { get; set; } = default!;

        [Parameter]
        public Action<IComponent> Capture { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<CascadingValue<SerginViewHost>>(3);
            builder.AddComponentParameter(4, nameof(CascadingValue<SerginViewHost>.Value), Host);
            builder.AddComponentParameter(5, nameof(CascadingValue<SerginViewHost>.ChildContent), (RenderFragment)(content =>
            {
                content.OpenComponent<TView>(0);

                foreach ((string name, object? value) in ViewParameters)
                {
                    content.AddComponentParameter(1, name, value);
                }

                content.AddComponentReferenceCapture(2, instance => Capture((IComponent)instance));
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
