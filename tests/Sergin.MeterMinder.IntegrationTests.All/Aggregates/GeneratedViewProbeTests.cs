using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Times;
using Sergin.SharedKernel.Presentation.Blazor.Validation;

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
            ViewProbeNoteTestHost.AddTo(services);
            services.Replace(ServiceDescriptor.Scoped<IUiTimeZoneStore, FixedTimeZoneStore>());
        }));

        users = host.Services.GetRequiredService<ProbeUserContextFactory>();
        store = host.Services.GetRequiredService<ViewProbeStore>();
        plainStore = host.Services.GetRequiredService<PlainProbeStore>();
        editStore = host.Services.GetRequiredService<EditProbeStore>();
        publishStore = host.Services.GetRequiredService<PublishProbeStore>();
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

        SetPropertyStatus status = await setProperty.SubmitAsync(dispatcher, retitle, probe, "Too late", loaded.Version);

        Assert.Equal(SetPropertyStatus.Stale, status);
        Assert.Contains(setProperty.Errors, VersionErrors.IsStale);
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
        SetPropertyStatus status = await setProperty.SubmitAsync(dispatcher, color, probe, ProbeColor.Red, loaded.Version);

        Assert.Equal(SetPropertyStatus.Sent, status);
        Assert.Empty(setProperty.Errors);
        Assert.Equal(ProbeColor.Red, editStore.Find(probe)!.Value.Record.Color);
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
        SetPropertyStatus status = await setProperty.SubmitAsync(dispatcher, rename, probe, string.Empty, loaded.Version);

        string message = Assert.Single(messages);
        Assert.Contains("Name", message, StringComparison.Ordinal);
        Assert.Equal(SetPropertyStatus.Failed, status);
        Assert.Equal(message, Assert.Single(setProperty.Errors).Description);
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

    private static IReadOnlyList<SerginPageAction> RowActions(
        AggregateViewModel model, Guid id, IReadOnlySet<CommandRole>? roles, object? record, ViewLinkPlacement links) =>
        new GeneratedCommandActions(new NoopReceiver(), _ => Task.CompletedTask, _ => Task.CompletedTask)
            .For(model, id, _ => [], roles, record: record, links: links);

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
}
