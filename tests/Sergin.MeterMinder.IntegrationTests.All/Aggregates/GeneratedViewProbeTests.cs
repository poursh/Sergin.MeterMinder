using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Times;
using Sergin.SharedKernel.Presentation.Blazor.Validation;
using Sergin.SharedKernel.Presentation.Blazor.Views;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The generated detail view as the prerender draws it on the test-only <c>/tst/view-probes/{id}</c> page: the fields
/// from the GetOne response, the header buttons, the actions from the probe's commands (hidden without their
/// permission), the split button and scheduled-tasks section of a schedulable command, and the not-found panel.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedViewProbeTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = default!;
    private ProbeUserContextFactory users = default!;
    private RbacScenario tree = default!;
    private ViewProbeStore store = default!;
    private PlainProbeStore plainStore = default!;
    private EditProbeStore editStore = default!;
    private PublishProbeStore publishStore = default!;
    private ConfirmProbeStore confirmStore = default!;
    private NotifiedErrors notified = default!;

    public async Task InitializeAsync()
    {
        host = ScheduledTaskTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            ProbeHandlers.AddTo(services);
            ScheduleProbeUiModule.AddTo(services);
            ProbeUserContextFactory.AddTo(services);
            ViewProbeTestHost.AddTo(services);
            PlainProbeTestHost.AddTo(services);
            EditProbeTestHost.AddTo(services);
            PublishProbeTestHost.AddTo(services);
            ConfirmProbeTestHost.AddTo(services);
            ViewProbeNoteTestHost.AddTo(services);
            services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
            services.AddSingleton<NotifiedErrors>();
            services.Replace(ServiceDescriptor.Scoped<IUiErrorPresenter>(provider => new RecordingErrorPresenter(
                ActivatorUtilities.CreateInstance<MudUiErrorPresenter>(provider), provider.GetRequiredService<NotifiedErrors>())));
        }));

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        store = host.Services.GetRequiredService<ViewProbeStore>();
        plainStore = host.Services.GetRequiredService<PlainProbeStore>();
        editStore = host.Services.GetRequiredService<EditProbeStore>();
        publishStore = host.Services.GetRequiredService<PublishProbeStore>();
        confirmStore = host.Services.GetRequiredService<ConfirmProbeStore>();
        notified = host.Services.GetRequiredService<NotifiedErrors>();
        await ScheduledTaskTestHost.ResetSchemaAsync(host);
        tree = await RbacScenario.BuildAsync(host);
    }

    public async Task DisposeAsync() => await host.DisposeAsync();

    [Fact]
    public async Task ProbeDetail_RendersFieldsHeaderAndActions()
    {
        Guid probe = store.Add("Probe alpha");
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Probe alpha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewProbeName", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Copy link\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Information\"", html, StringComparison.Ordinal);
        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.Contains(">Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_OverviewSlot_ReceivesLoadedOverview()
    {
        Guid probe = store.Add("Probe overview");
        await GrantAsync(ViewProbePermissions.Read);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains($"probe-figure:{GetViewProbeOverviewQueryHandler.Figure}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_SchedulableCommand_RendersSplitButtonWithScheduleItems()
    {
        Guid probe = store.Add("Probe beta");
        await GrantAsync(ViewProbePermissions.Run, ScheduledTaskPermissions.Manage);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"More actions\"", html, StringComparison.Ordinal);
        Assert.Contains("Scheduled tasks", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_WithoutManagePermission_RendersPlainRunButton()
    {
        Guid probe = store.Add("Probe gamma");
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Run probe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("More actions", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_WithoutRunPermission_HidesRunButton()
    {
        Guid probe = store.Add("Probe delta");
        await GrantAsync(ViewProbePermissions.Read);

        string html = await GetAsync($"/tst/view-probes/{probe}");

        Assert.Contains("Probe delta", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Run probe", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProbeDetail_UnknownId_ShowsProblemPanel()
    {
        await GrantAsync(ViewProbePermissions.Run);

        string html = await GetAsync($"/tst/view-probes/{Guid.CreateVersion7()}");

        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Run probe", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeDetail_NotVersioned_RendersRecord()
    {
        Guid probe = plainStore.Add("Plain alpha");

        string html = await GetAsync($"/tst/plain-probes/{probe}");

        Assert.Contains("Plain alpha", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Reload\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_RendersEnumValueText_NotMemberName()
    {
        Guid probe = plainStore.Add("Plain enum");

        string html = await GetAsync($"/tst/plain-probes/{probe}");

        Assert.Contains("Runner-up value", html, StringComparison.Ordinal);
        Assert.DoesNotContain($">{nameof(ProbeEnum.Second)}<", html, StringComparison.Ordinal);
        Assert.Contains("Switched off", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_ShowsFieldHint()
    {
        Guid probe = plainStore.Add("Plain hint");

        string html = await GetAsync($"/tst/plain-probes/{probe}/edit");

        Assert.Contains("Probe hint text", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeEdit_NotVersioned_RendersPrefilledForm()
    {
        Guid probe = plainStore.Add("Plain beta");

        string html = await GetAsync($"/tst/plain-probes/{probe}/edit");

        Assert.Contains("value=\"Plain beta\"", html, StringComparison.Ordinal);
        Assert.Contains(">Save<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainProbeEdit_NotVersioned_LoadsAndSubmitsWithoutAVersion()
    {
        Guid probe = plainStore.Add("Plain gamma");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<PlainProbe>();
        Assert.False(model.Features.Versioned);

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);

        Assert.False(loaded.IsError, loaded.IsError ? loaded.Errors[0].Code : null);
        Assert.Null(loaded.Version);

        Dictionary<string, object?> values = new(StringComparer.Ordinal) { [nameof(UpdatePlainProbeCommand.Name)] = "Plain delta" };
        ViewSendResult saved = await model.SubmitFormAsync(dispatcher, probe, values, loaded.Version);

        Assert.False(saved.IsError, saved.IsError ? saved.Errors[0].Code : null);
        Assert.Equal("Plain delta", plainStore.Find(probe)!.Name.Value);
    }

    [Fact]
    public async Task GeneratedSetProperty_Stale_ReportsStaleAndSendsOnce()
    {
        Guid probe = editStore.Add("Stale probe");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<EditProbe>();
        CommandView retitle = model.Commands.Single(command => command.Request == typeof(RetitleProbeCommand));

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        GeneratedSetProperty setProperty = new(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());

        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);
        Assert.NotNull(loaded.Version);
        editStore.Touch(probe);
        int before = editStore.Sends;

        SetPropertyOutcome outcome = await setProperty.SendAsync(dispatcher, retitle, probe, "Too late", loaded.Version);

        Assert.Equal(SetPropertyStatus.Stale, outcome.Status);
        Assert.Contains(outcome.Errors, VersionErrors.IsStale);
        Assert.Equal(before + 1, editStore.Sends);
        Assert.Equal("Headline Stale probe", editStore.Find(probe)!.Value.Record.Title.Value);
    }

    [Fact]
    public async Task GeneratedSetProperty_AtTheCurrentVersion_Sends()
    {
        Guid probe = editStore.Add("Sent probe");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<EditProbe>();
        CommandView color = model.Commands.Single(command => command.Request == typeof(SetProbeColorCommand));

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        GeneratedSetProperty setProperty = new(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());

        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);
        SetPropertyOutcome outcome = await setProperty.SendAsync(dispatcher, color, probe, ProbeColor.Red, loaded.Version);

        Assert.Equal(SetPropertyStatus.Sent, outcome.Status);
        Assert.Empty(outcome.Errors);
        Assert.Equal(ProbeColor.Red, editStore.Find(probe)!.Value.Record.Color);
    }

    [Fact]
    public async Task GeneratedSetProperty_TwoSends_EachOutcomeCarriesItsOwnErrors()
    {
        Guid probe = editStore.Add("Overlap probe");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<EditProbe>();
        CommandView rename = model.Commands.Single(command => command.Request == typeof(RenameProbeCommand));
        CommandView retitle = model.Commands.Single(command => command.Request == typeof(RetitleProbeCommand));

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        GeneratedSetProperty setProperty = new(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());

        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);
        editStore.Touch(probe);

        // One instance, two sends in flight at once: a blank name the validator refuses, a title at a version gone stale.
        Task<SetPropertyOutcome> invalidSend = setProperty.SendAsync(dispatcher, rename, probe, string.Empty, loaded.Version);
        Task<SetPropertyOutcome> staleSend = setProperty.SendAsync(dispatcher, retitle, probe, "Too late", loaded.Version);
        SetPropertyOutcome[] outcomes = await Task.WhenAll(invalidSend, staleSend);

        Assert.Equal(SetPropertyStatus.Invalid, outcomes[0].Status);
        Error invalid = Assert.Single(outcomes[0].Errors);
        Assert.Equal(nameof(RenameProbeCommand.Name), invalid.Code);
        Assert.False(VersionErrors.IsStale(invalid));

        Assert.Equal(SetPropertyStatus.Stale, outcomes[1].Status);
        Assert.NotEmpty(outcomes[1].Errors);
        Assert.All(outcomes[1].Errors, error => Assert.True(VersionErrors.IsStale(error), error.Code));
    }

    [Fact]
    public async Task Detail_DialogEdit_SaveStaleCancel()
    {
        Guid probe = editStore.Add("Dialog probe");
        await GrantAsync(EditProbePermissions.Read, EditProbePermissions.Update);
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<EditProbe>();
        CommandView retitle = model.Commands.Single(command => command.Request == typeof(RetitleProbeCommand));
        Assert.Equal(SerginEditMode.Dialog, retitle.EditMode);

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/tst/edit-probes/{probe}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            EditDetailHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<EditDetailHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(EditDetailHost.Id)] = probe }));
            SerginGeneratedDetailView<EditProbe> view = EditDetailHost.View!;
            Assert.Contains("Headline Dialog probe", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            // Save a valid value: sent once, the dialog closes, and the view shows the record read again.
            notified.Clear();
            int before = editStore.Sends;
            IDialogReference saved = await view.EditAsync(retitle);
            var dialog = (SerginSetPropertyDialog)saved.Dialog!;
            Assert.Equal("Headline Dialog probe", dialog.Value?.ToString());

            await dialog.SubmitAsync("Fresh headline");

            Assert.Equal(before + 1, editStore.Sends);
            Assert.True(saved.Result.IsCompleted);
            Assert.False((await saved.Result)!.Canceled);
            Assert.Equal("Fresh headline", editStore.Find(probe)!.Value.Record.Title.Value);
            Assert.Contains("Fresh headline", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
            Assert.Empty(notified.Errors);

            // Save at a stale version: the dialog stays open and the stale error is shown once.
            IDialogReference stale = await view.EditAsync(retitle);
            var staleDialog = (SerginSetPropertyDialog)stale.Dialog!;
            editStore.Touch(probe);

            await staleDialog.SubmitAsync("Too late");

            Assert.False(stale.Result.IsCompleted);
            Error shown = Assert.Single(notified.Errors);
            Assert.True(VersionErrors.IsStale(shown));
            Assert.Equal(1, notified.Calls);
            Assert.Equal("Fresh headline", editStore.Find(probe)!.Value.Record.Title.Value);

            // Cancel the still-open dialog: it closes cancelled and sends nothing. (Each ShowAsync waits about 5 s for a
            // render-complete signal the in-process renderer never gives, so the test opens the dialog only twice.)
            int beforeCancel = editStore.Sends;

            staleDialog.Cancel();

            Assert.True((await stale.Result)!.Canceled);
            Assert.Equal(beforeCancel, editStore.Sends);
            Assert.Single(notified.Errors);
        });
    }

    [Fact]
    public async Task InlineEdit_InvalidValue_SendsNothing()
    {
        Guid probe = editStore.Add("Valid probe");
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<EditProbe>();
        CommandView rename = model.Commands.Single(command => command.Request == typeof(RenameProbeCommand));

        using IServiceScope scope = host.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        GeneratedSetProperty setProperty = new(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());
        ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, probe);
        int before = editStore.Sends;

        // What the value form's field validation shows, then what a submit of that value does.
        IReadOnlyCollection<string> messages = await setProperty.ValidateAsync(rename, probe, string.Empty);
        SetPropertyOutcome outcome = await setProperty.SendAsync(dispatcher, rename, probe, string.Empty, loaded.Version);

        string message = Assert.Single(messages);
        Assert.Contains("Name", message, StringComparison.Ordinal);
        Assert.Equal(SetPropertyStatus.Invalid, outcome.Status);
        Assert.Equal(message, Assert.Single(outcome.Errors).Description);
        Assert.Equal(before, editStore.Sends);
        Assert.Equal("Valid probe", editStore.Find(probe)!.Value.Record.Name.Value);
    }

    [Fact]
    public async Task Detail_InlineCardAndChoice_ShownToManager_HiddenFromReader()
    {
        Guid probe = editStore.Add("Inline probe");

        await GrantAsync(EditProbePermissions.Read, EditProbePermissions.Update);
        string manager = await GetAsync($"/tst/edit-probes/{probe}");

        Assert.Contains(">Rename<", manager, StringComparison.Ordinal);
        Assert.Contains("value=\"Inline probe\"", manager, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Color\"", manager, StringComparison.Ordinal);
        Assert.Contains("Emerald", manager, StringComparison.Ordinal);

        await GrantAsync(EditProbePermissions.Read);
        string reader = await GetAsync($"/tst/edit-probes/{probe}");

        Assert.Contains("Inline probe", reader, StringComparison.Ordinal);
        Assert.Contains("Emerald", reader, StringComparison.Ordinal);
        Assert.DoesNotContain(">Rename<", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Inline probe\"", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Color\"", reader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_DialogCommand_IsABarAction()
    {
        Guid probe = editStore.Add("Dialog probe");

        await GrantAsync(EditProbePermissions.Read, EditProbePermissions.Update);
        string manager = await GetAsync($"/tst/edit-probes/{probe}");

        await GrantAsync(EditProbePermissions.Read);
        string reader = await GetAsync($"/tst/edit-probes/{probe}");

        // The field's caption shows to both; the Headline action (Retitle's default label) only to a holder of Update.
        Assert.Contains("Headline Dialog probe", reader, StringComparison.Ordinal);
        Assert.Equal(CountOf(reader, ">Headline<") + 1, CountOf(manager, ">Headline<"));
    }

    [Fact]
    public async Task Detail_ReversePair_ShowsOnlyTheOneThatApplies()
    {
        Guid draft = publishStore.Add("Draft probe", published: false);
        Guid live = publishStore.Add("Live probe", published: true);

        string draftHtml = await GetAsync($"/tst/publish-probes/{draft}");
        string liveHtml = await GetAsync($"/tst/publish-probes/{live}");

        Assert.Contains(">Publish<", draftHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Unpublish", draftHtml, StringComparison.Ordinal);
        Assert.Contains(">Unpublish<", liveHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Publish<", liveHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void List_ReversePair_NotOffered()
    {
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<PublishProbe>();
        var item = new PublishProbeListItem(Guid.CreateVersion7(), "Row", Published: false);

        // The row is not the record the pair reads, so the menu (where a transition goes) holds neither command.
        IReadOnlyList<SerginPageAction> menu = RowActions(model, item.Id, GeneratedLists.Menu, item, ViewLinkPlacement.None);

        Assert.Empty(menu);
    }

    [Fact]
    public async Task Detail_Link_IsSecondaryActionWithHref()
    {
        Guid probe = publishStore.Add("Linked probe", published: false);

        string html = await GetAsync($"/tst/publish-probes/{probe}");

        Assert.Contains($"href=\"/tst/publish-probes/{probe}/translations\"", html, StringComparison.Ordinal);
        Assert.Contains("Translations", html, StringComparison.Ordinal);

        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<PublishProbe>();
        IReadOnlyList<SerginPageAction> actions = RowActions(model, probe, null, publishStore.Find(probe), ViewLinkPlacement.AfterCommands);

        // Publish (Primary), then the link, which carries no permission.
        Assert.Equal([PublishProbeTexts.Publish, PublishProbeTexts.Translations], actions.Select(action => action.Label));
        SerginPageAction link = actions[^1];
        Assert.Equal(SerginActionKind.Secondary, link.Kind);
        Assert.Equal($"/tst/publish-probes/{probe}/translations", link.Href);
        Assert.Null(link.Permission);
    }

    [Fact]
    public void List_Link_IsRowIcon()
    {
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<PublishProbe>();
        var item = new PublishProbeListItem(Guid.CreateVersion7(), "Row", Published: false);

        IReadOnlyList<SerginPageAction> icons = RowActions(model, item.Id, GeneratedLists.Icons, item, ViewLinkPlacement.BeforeCommands);

        SerginPageAction link = Assert.Single(icons);
        Assert.Equal(PublishProbeTexts.Translations, link.Label);
        Assert.Equal(MudBlazor.Icons.Material.Outlined.Translate, link.Icon);
        Assert.Equal($"/tst/publish-probes/{item.Id}/translations", link.Href);
    }

    [Fact]
    public async Task List_UnderParent_TrailHasParentRecordStep()
    {
        Guid probe = store.Add("Noted probe");

        string html = await GetAsync($"/tst/view-probes/{probe}/notes");

        Assert.Contains("<title>Probe notes</title>", html, StringComparison.Ordinal);

        // The parent's list step, the parent as stored linking to its detail, then this list's title.
        Assert.Contains("href=\"/tst/view-probes\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/tst/view-probes/{probe}\"", html, StringComparison.Ordinal);
        Assert.Contains(">Noted probe<", html, StringComparison.Ordinal);
        Assert.Contains(">Probe notes<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_UnderParent_UnknownParent_KeepsPlaceholderStep()
    {
        var unknown = Guid.CreateVersion7();

        string html = await GetAsync($"/tst/view-probes/{unknown}/notes");

        // The parent's singular, still linking to where the parent would be; the page renders.
        Assert.Contains($"href=\"/tst/view-probes/{unknown}\"", html, StringComparison.Ordinal);
        Assert.Contains(">View probe<", html, StringComparison.Ordinal);
        Assert.Contains($"note-parent-id:{unknown}", html, StringComparison.Ordinal);
        Assert.Contains("note-parent-name:<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_BodySlot_ReplacesTableAndGetsParent()
    {
        Guid probe = store.Add("Slotted probe");

        string html = await GetAsync($"/tst/view-probes/{probe}/notes");

        Assert.Contains($"note-parent-id:{probe}", html, StringComparison.Ordinal);
        Assert.Contains("note-parent-name:Slotted probe", html, StringComparison.Ordinal);
        Assert.Contains("note-generation:0", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_UnderParent_NewParent_ReloadsTableUnderIt()
    {
        Guid first = store.Add("First parent");
        Guid second = store.Add("Second parent");
        await GrantAsync(ViewProbePermissions.Read);
        ViewProbeNoteQueries queries = host.Services.GetRequiredService<ViewProbeNoteQueries>();
        queries.ParentIds.Clear();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/tst/view-probes/{first}/notes");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            NoteListHost.Current = null;
            await renderer.RenderComponentAsync<NoteListHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NoteListHost.ParentId)] = first,
            }));

            // The table's first load, then the same table handed another parent, as a page reused across a navigation is.
            await NoteListHost.Body!.ReloadAsync();
            await NoteListHost.Current!.ShowAsync(second);
        });

        Assert.Equal([first, second], queries.ParentIds);
    }

    [Fact]
    public async Task Detail_ShownIdField_RendersWithItsLabel()
    {
        Guid probe = confirmStore.Add("Shown probe");
        await GrantAsync(ConfirmProbePermissions.Read);

        await RenderConfirmDetailAsync(probe, (root, _, _) =>
        {
            string html = WebUtility.HtmlDecode(root.ToHtmlString());

            // A Guid named Id is hidden by default; Shown() puts it among the fields under its declared label.
            Assert.Contains(">Reference<", html, StringComparison.Ordinal);
            Assert.Contains(probe.ToString(), html, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Transition_WithConfirm_AsksFirst()
    {
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ConfirmProbe>();
        var record = new ConfirmProbeResponse(Guid.CreateVersion7(), "Asked probe", new ConfirmProbeTitle("Headline"));
        List<(CommandView Command, object? Record)> asked = [];
        bool answer = false;
        int runs = 0;
        var actions = new GeneratedCommandActions(
            new InvokingReceiver(),
            _ =>
            {
                runs++;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            confirmAsync: (command, shown) =>
            {
                asked.Add((command, shown));
                return Task.FromResult(answer);
            });

        SerginPageAction archive = actions.For(model, record.Id, _ => [], record: record).Single(action => action.Label == ConfirmProbeTexts.Archive);

        // Declined: asked once, nothing sent.
        await archive.OnClick.InvokeAsync();
        Assert.Equal(0, runs);

        // Accepted: asked again, sent once.
        answer = true;
        await archive.OnClick.InvokeAsync();
        Assert.Equal(1, runs);

        Assert.Equal(2, asked.Count);
        Assert.All(asked, ask =>
        {
            Assert.Equal(typeof(ArchiveConfirmProbeCommand), ask.Command.Request);
            Assert.Same(record, ask.Record);
        });
    }

    [Fact]
    public void List_DeleteWithValues_IsNotARowOrBulkAction()
    {
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ConfirmProbe>();
        CommandView delete = model.Commands.Single(command => command.Request == typeof(DeleteConfirmProbeCommand));
        var item = new ConfirmProbeListItem(Guid.CreateVersion7(), "Row");
        var actions = new GeneratedCommandActions(new NoopReceiver(), _ => Task.CompletedTask, _ => Task.CompletedTask);

        IReadOnlyList<SerginPageAction> icons = actions.For(model, item.Id, _ => [], GeneratedLists.Icons, record: item, row: true);
        IReadOnlyList<SerginPageAction> bar = actions.For(model, item.Id, _ => [], record: item);

        // A row has no dialog to ask for the value, so its icons hold no Delete; the detail bar still offers it.
        Assert.DoesNotContain(icons, action => action.Label == delete.Label);
        Assert.Contains(bar, action => action.Label == delete.Label);
        Assert.True(delete.Selectable);
        Assert.False(GeneratedLists.IsBulkAction(delete));
    }

    [Fact]
    public async Task DeleteWithValues_Refused_StaysAndReloads()
    {
        Guid probe = confirmStore.Add("Holding probe");
        await GrantAsync(ConfirmProbePermissions.Read, ConfirmProbePermissions.Update);
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ConfirmProbe>();
        CommandView delete = model.Commands.Single(command => command.Request == typeof(DeleteConfirmProbeCommand));

        await RenderConfirmDetailAsync(probe, async (root, view, viewHost) =>
        {
            // The confirm asks for the value: the text filled from the record, the Cascade switch, the command's label.
            IDialogReference asking = await view.AskValuesAsync(delete);
            var dialog = (SerginConfirmValuesDialog)asking.Dialog!;
            string html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Delete Holding probe for good?", html, StringComparison.Ordinal);
            Assert.Contains("Also delete what it holds", html, StringComparison.Ordinal);

            dialog.Confirm();

            DialogResult answered = (await asking.Result)!;
            Assert.False(answered.Canceled);
            var values = (IReadOnlyDictionary<string, object?>)answered.Data!;

            // Refused (no Cascade): the error shown once, the view stays and reads the record again.
            notified.Clear();
            int loads = confirmStore.Loads;
            int sends = confirmStore.Sends;

            await view.DeleteWithValuesAsync(delete, values);

            Assert.Equal(sends + 1, confirmStore.Sends);
            Error shown = Assert.Single(notified.Errors);
            Assert.Equal(ConfirmProbeStore.HoldsRecords.Code, shown.Code);
            Assert.Equal(1, notified.Calls);
            Assert.Empty(viewHost.Leaves);
            Assert.True(confirmStore.Loads > loads);
            Assert.NotNull(confirmStore.Find(probe));

            // With Cascade: sent at the version just read, the record gone, and the view leaves for the list.
            await view.DeleteWithValuesAsync(delete, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(DeleteConfirmProbeCommand.Cascade)] = true });

            Assert.Null(confirmStore.Find(probe));
            Assert.Equal([ConfirmProbeRoutes.List], viewHost.Leaves);
            Assert.Equal(1, notified.Calls);
        });
    }

    [Fact]
    public async Task Detail_HeaderReload_BumpsGeneration()
    {
        Guid probe = confirmStore.Add("Reloaded probe");
        await GrantAsync(ConfirmProbePermissions.Read);

        await RenderConfirmDetailAsync(probe, async (_, view, viewHost) =>
        {
            int generation = view.Generation;
            int loads = confirmStore.Loads;

            await view.HeaderReloadAsync();

            Assert.Equal(generation + 1, view.Generation);
            Assert.True(confirmStore.Loads > loads);
            Assert.Equal(0, viewHost.Changes);
        });
    }

    [Fact]
    public async Task Detail_SetPropertySent_CallsHostChanged()
    {
        Guid probe = confirmStore.Add("Changed probe");
        await GrantAsync(ConfirmProbePermissions.Read, ConfirmProbePermissions.Update);
        AggregateViewModel model = host.Services.GetRequiredService<AggregateViewRegistry>().For<ConfirmProbe>();
        CommandView retitle = model.Commands.Single(command => command.Request == typeof(RetitleConfirmProbeCommand));
        CommandView archive = model.Commands.Single(command => command.Request == typeof(ArchiveConfirmProbeCommand));

        await RenderConfirmDetailAsync(probe, async (root, view, viewHost) =>
        {
            int generation = view.Generation;

            // Sent: the generation moves, the host hears of it once, and the view shows the record read again.
            Assert.True(await view.SetPropertyAsync(retitle, "Fresh headline"));
            Assert.Equal(1, viewHost.Changes);
            Assert.Equal(generation + 1, view.Generation);
            Assert.Contains("Fresh headline", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);

            // Stale: nothing changed, so neither moves.
            confirmStore.Touch(probe);
            Assert.False(await view.SetPropertyAsync(retitle, "Too late"));
            Assert.Equal(1, viewHost.Changes);
            Assert.Equal(generation + 1, view.Generation);

            // A transition that keeps the view open tells the host too.
            await view.RunAsync(archive);
            Assert.Contains(probe, confirmStore.Archived);
            Assert.Equal(2, viewHost.Changes);
            Assert.Equal(generation + 2, view.Generation);
        });
    }

    private static IReadOnlyList<SerginPageAction> RowActions(
        AggregateViewModel model, Guid id, IReadOnlySet<CommandRole>? roles, object? record, ViewLinkPlacement links) =>
        new GeneratedCommandActions(new NoopReceiver(), _ => Task.CompletedTask, _ => Task.CompletedTask)
            .For(model, id, _ => [], roles, record: record, links: links);

    // Renders the confirm probe's detail view, with the providers its dialogs render into and a recording host, and hands
    // the test the root, the view and the host on the renderer's dispatcher.
    private async Task RenderConfirmDetailAsync(Guid probe, Func<HtmlRootComponent, SerginGeneratedDetailView<ConfirmProbe>, RecordingViewHost, Task> test)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = users.Current!;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost{ConfirmProbeRoutes.Detail(probe)}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var viewHost = new RecordingViewHost();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ConfirmDetailHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<ConfirmDetailHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(ConfirmDetailHost.Id)] = probe,
                    [nameof(ConfirmDetailHost.ViewHost)] = viewHost.Host,
                }));

            await test(root, ConfirmDetailHost.View!, viewHost);
        });
    }

    /// <summary>Runs every event callback it receives, as a component's dispatcher does.</summary>
    private sealed class InvokingReceiver : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
    }

    /// <summary>A view host that records where the view left to and how often it said it changed its record.</summary>
    private sealed class RecordingViewHost
    {
        private readonly List<string> leaves = [];

        public RecordingViewHost() =>
            Host = new SerginViewHost(
                SerginViewHostKind.Embedded,
                href =>
                {
                    leaves.Add(href);
                    return Task.CompletedTask;
                },
                headerSlot: null,
                changed: () =>
                {
                    Changes++;
                    return Task.CompletedTask;
                });

        public SerginViewHost Host { get; }

        public IReadOnlyList<string> Leaves => leaves;

        public int Changes { get; private set; }
    }

    /// <summary>The confirm probe's generated detail view under a given host, with the providers its dialogs render into.</summary>
    private sealed class ConfirmDetailHost : ComponentBase
    {
        public static SerginGeneratedDetailView<ConfirmProbe>? View { get; set; }

        [Parameter]
        public Guid Id { get; set; }

        [Parameter]
        public SerginViewHost ViewHost { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<CascadingValue<SerginViewHost>>(2);
            builder.AddComponentParameter(3, nameof(CascadingValue<SerginViewHost>.Value), ViewHost);
            builder.AddComponentParameter(4, nameof(CascadingValue<SerginViewHost>.ChildContent), (RenderFragment)(content =>
            {
                content.OpenComponent<SerginGeneratedDetailView<ConfirmProbe>>(0);
                content.AddComponentParameter(1, nameof(SerginGeneratedDetailView<ConfirmProbe>.Id), Id);
                content.AddComponentReferenceCapture(2, instance => View = (SerginGeneratedDetailView<ConfirmProbe>)instance);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class NoopReceiver : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => Task.CompletedTask;
    }

    private static int CountOf(string html, string text) =>
        (html.Length - html.Replace(text, string.Empty, StringComparison.Ordinal).Length) / text.Length;

    // The grant is made as the host's own user, so a second grant in one test is not made by the caller of the first.
    private async Task GrantAsync(params PermissionDefinition[] permissions)
    {
        users.Current = null;
        users.Current = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, [.. permissions.Select(permission => permission.Permission)]);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        string body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {response.StatusCode}: {body[..Math.Min(body.Length, 3000)]}");

        return body;
    }

    /// <summary>Every error the views handed to the error presenter, and how many times it was called.</summary>
    private sealed class NotifiedErrors
    {
        private readonly List<Error> errors = [];
        private int calls;

        public IReadOnlyList<Error> Errors
        {
            get
            {
                lock (errors)
                {
                    return [.. errors];
                }
            }
        }

        public int Calls => Volatile.Read(ref calls);

        public void Add(IReadOnlyList<Error> notified)
        {
            Interlocked.Increment(ref calls);

            lock (errors)
            {
                errors.AddRange(notified);
            }
        }

        public void Clear()
        {
            Volatile.Write(ref calls, 0);

            lock (errors)
            {
                errors.Clear();
            }
        }
    }

    private sealed class RecordingErrorPresenter(IUiErrorPresenter inner, NotifiedErrors notified) : IUiErrorPresenter
    {
        public SerginProblem Present(Error error) => inner.Present(error);

        public void Notify(Error error)
        {
            notified.Add([error]);
            inner.Notify(error);
        }

        public void Notify(IReadOnlyList<Error> errors)
        {
            notified.Add(errors);
            inner.Notify(errors);
        }
    }

    /// <summary>The edit probe's generated detail view with the providers its dialogs render into, and a handle on the view.</summary>
    private sealed class EditDetailHost : ComponentBase
    {
        public static SerginGeneratedDetailView<EditProbe>? View { get; set; }

        [Parameter]
        public Guid Id { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<SerginGeneratedDetailView<EditProbe>>(2);
            builder.AddComponentParameter(3, nameof(SerginGeneratedDetailView<EditProbe>.Id), Id);
            builder.AddComponentReferenceCapture(4, instance => View = (SerginGeneratedDetailView<EditProbe>)instance);
            builder.CloseComponent();
        }
    }

    /// <summary>The generated table of the probe's notes under a parent the test can change, with the providers it renders into.</summary>
    private sealed class NoteListHost : ComponentBase
    {
        private Guid? shown;

        public static NoteListHost? Current { get; set; }

        public static GeneratedListBody<ViewProbeNoteListItem>? Body { get; set; }

        [Parameter]
        public Guid ParentId { get; set; }

        [Inject]
        private AggregateViewRegistry Views { get; set; } = default!;

        /// <summary>Hands the table another parent, as a parent component re-rendering with a new parameter does.</summary>
        public Task ShowAsync(Guid parentId)
        {
            shown = parentId;
            return InvokeAsync(StateHasChanged);
        }

        protected override void OnInitialized() => Current = this;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<GeneratedListBody<ViewProbeNoteListItem>>(1);
            builder.AddComponentParameter(2, nameof(GeneratedListBody<ViewProbeNoteListItem>.Model), Views.For<ViewProbeNote>());
            builder.AddComponentParameter(3, nameof(GeneratedListBody<ViewProbeNoteListItem>.ParentId), shown ?? ParentId);
            builder.AddComponentReferenceCapture(4, instance => Body = (GeneratedListBody<ViewProbeNoteListItem>)instance);
            builder.CloseComponent();
        }
    }
}
