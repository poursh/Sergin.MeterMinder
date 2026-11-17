using System.Net;
using ErrorOr;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Application.Configurations.Scopes;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Create;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Delete;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.GetOne;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Rename;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.Administration.UserAccess.Presentation.Blazor.Scopes;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Views;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated Scope views show inside the scope tree's pane: the fields, the parent link and the relations slot,
/// an inline rename and a move that tell the tree to reload, a delete that asks for cascade and leaves for the parent,
/// no move or delete on the root, nothing to manage for a reader, and the two header links.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedScopeViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task ScopeDetail_InThePane_RendersFieldsRelationsAndHeaderSlotButtons()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.North}");

        Assert.Contains("North Region", html, StringComparison.Ordinal);
        Assert.Contains(">Under<", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/scopes/{scenario.Company}\"", html, StringComparison.Ordinal);

        // The relations slot: the child scopes (linking) and the roles usable here.
        Assert.Contains("Child scopes", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/scopes/{scenario.Feeder1}\"", html, StringComparison.Ordinal);
        Assert.Contains("Roles usable here", html, StringComparison.Ordinal);

        // The header: reload and the actions.
        Assert.Contains("Reload", html, StringComparison.Ordinal);
        Assert.Contains("New child scope", html, StringComparison.Ordinal);
        Assert.Contains("Move", html, StringComparison.Ordinal);
        Assert.Contains("Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeDetail_RenameInline_ReloadsTheTree()
    {
        CommandView rename = Model().Commands.Single(command => command.Request == typeof(RenameScopeCommand));
        Assert.Equal(SerginEditMode.Inline, rename.EditMode);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid scope = await scenario.CreateScopeAsync(scenario.North, $"rename-{Guid.NewGuid():N}"[..16]);
        string renamed = $"renamed-{Guid.NewGuid():N}"[..16];

        await RenderDetailAsync(scope, caller: null, async (_, view, viewHost) =>
        {
            Assert.True(await view.SetPropertyAsync(rename, renamed));

            Assert.Equal(1, viewHost.Changes);
        });

        Assert.Equal(renamed, (await LoadScopeAsync(scope)).Name);
    }

    [Fact]
    public async Task ScopeDetail_Move_UnderAnotherScope_ReloadsTheTree()
    {
        CommandView move = Model().Commands.Single(command => command.Request == typeof(MoveScopeCommand));
        Assert.NotNull(move.BuildMove);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid scope = await scenario.CreateScopeAsync(scenario.North, $"move-{Guid.NewGuid():N}"[..16]);

        await RenderDetailAsync(scope, caller: null, async (_, view, viewHost) =>
        {
            IDialogReference asking = await view.AskParentAsync(move);
            Assert.NotNull(asking.Dialog);

            await view.MoveToAsync(move, scenario.South);

            Assert.Equal(1, viewHost.Changes);
        });

        Assert.Equal(scenario.South, (await LoadScopeAsync(scope)).ParentId);
    }

    [Fact]
    public async Task ScopeDetail_Delete_AsksForCascade_AndLeavesToTheParent()
    {
        CommandView delete = Model().Commands.Single(command => command.Request == typeof(DeleteScopeCommand));

        FormFieldView cascade = Assert.Single(delete.ConfirmFields);
        Assert.Equal(nameof(DeleteScopeCommand.Cascade), cascade.Name);
        Assert.Equal(ScopeTexts.Cascade, cascade.Label);
        Assert.Equal(ScopeUiTexts.DeleteConfirm, delete.ConfirmText);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid scope = await scenario.CreateScopeAsync(scenario.North, $"delete-{Guid.NewGuid():N}"[..16]);

        await RenderDetailAsync(scope, caller: null, async (root, view, viewHost) =>
        {
            IDialogReference asking = await view.AskValuesAsync(delete);
            var dialog = (SerginConfirmValuesDialog)asking.Dialog!;
            Assert.Contains(ScopeTexts.Cascade.Label.Default, Html(root), StringComparison.Ordinal);

            dialog.Confirm();
            DialogResult answered = (await asking.Result)!;
            Assert.False(answered.Canceled);

            await view.DeleteWithValuesAsync(delete, (IReadOnlyDictionary<string, object?>)answered.Data!);

            Assert.Equal([$"/ua/scopes/{scenario.North}"], viewHost.Leaves);
        });

        using IServiceScope check = factory.Services.CreateScope();
        Assert.True((await check.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendVersionedAsync(new GetScopeByIdQueryCommand(new ScopeId(scope)))).IsError);
    }

    [Fact]
    public async Task RootScopeDetail_Under_ShowsTheTreeRootText()
    {
        string html = await GetAsync($"/ua/scopes/{Scope.RootId.Value}");

        Assert.Contains("The root of the tree", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/ua/scopes/\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeDetail_Under_LinksToTheParent()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.Feeder1}");

        Assert.Contains($"href=\"/ua/scopes/{scenario.North}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeDetail_ChildScopes_ListsDescendantsIndented()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.Company}");

        Assert.Contains("Child scopes", html, StringComparison.Ordinal);
        int north = html.IndexOf($"href=\"/ua/scopes/{scenario.North}\"", StringComparison.Ordinal);
        int feeder = html.IndexOf($"href=\"/ua/scopes/{scenario.Feeder1}\"", StringComparison.Ordinal);
        int south = html.IndexOf($"href=\"/ua/scopes/{scenario.South}\"", StringComparison.Ordinal);
        Assert.True(north >= 0 && north < feeder && feeder < south);
        Assert.Equal(PaddingBefore(html, north) + 24, PaddingBefore(html, feeder));
    }

    [Fact]
    public void ScopeTree_Keys_MatchTheHandWrittenLayout()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        AggregateViewModel model = scope.ServiceProvider.GetRequiredService<AggregateViewRegistry>().For<Scope>();

        var parts = GeneratedTreeParts.For(model);

        Assert.Equal("ua.scopes", parts.LayoutKey);
        Assert.Equal("scope", parts.GroupKey);
        Assert.Equal(typeof(DeleteScopeCommand), parts.Delete!.Request);
        DeleteScopeCommand command = Assert.IsType<DeleteScopeCommand>(GeneratedTreeParts.DeleteRequest(parts.Delete, Guid.NewGuid()));
        Assert.False(command.Cascade);
    }

    private static int PaddingBefore(string html, int index)
    {
        const string marker = "padding-left: ";
        int at = html.LastIndexOf(marker, index, StringComparison.Ordinal) + marker.Length;
        return int.Parse(html.AsSpan(at, html.IndexOf("px", at, StringComparison.Ordinal) - at), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task RootScopeDetail_OffersNoMoveOrDelete()
    {
        AggregateViewModel model = Model();
        var root = new ScopeQueryResponse(Scope.RootId.Value, null, "Root", null);
        var child = new ScopeQueryResponse(Guid.NewGuid(), Guid.NewGuid(), "Child", "Parent");

        foreach (Type request in new[] { typeof(MoveScopeCommand), typeof(DeleteScopeCommand) })
        {
            Func<object, bool> applies = model.Commands.Single(view => view.Request == request).AppliesWhen!;

            Assert.False(applies(root));
            Assert.True(applies(child));
        }

        string html = await GetAsync($"/ua/scopes/{Scope.RootId.Value}");

        Assert.Contains("New child scope", html, StringComparison.Ordinal);
        Assert.Contains("The root of the tree", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Move<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Delete<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeDetail_ForAReader_HidesTheManageControls()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid scope = await scenario.CreateScopeAsync(scenario.North, $"reader-{Guid.NewGuid():N}"[..16]);
        TestCaller reader = await scenario.CreateGrantedCallerAsync(Scope.RootId.Value, ScopePermissions.Read.Permission);

        await RenderDetailAsync(scope, reader, (root, _, _) =>
        {
            string html = Html(root);
            Assert.Contains(">Under<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Rename<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Move<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Delete<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("New child scope", html, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ScopeDetail_NewChildLinksWithParentId_NewRoleHereLinksWithScopeId()
    {
        AggregateViewModel model = Model();
        var id = Guid.NewGuid();

        Assert.Equal($"/ua/scopes/new?parentId={id:D}", model.ChildCreateHref(id));

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        string html = await GetAsync($"/ua/scopes/{scenario.North}");

        Assert.Contains($"href=\"/ua/scopes/new?parentId={scenario.North}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/roles/new?scopeId={scenario.North}\"", html, StringComparison.Ordinal);
        Assert.Contains("New role here", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeCreate_TakesParentIdFromTheQuery_AndOpensTheNewScope()
    {
        AggregateViewModel model = Model();

        Assert.Equal(nameof(CreateScopeCommand.ParentId), model.CreateParent);
        Assert.Equal(ScopeUiTexts.New, model.CreateTitle);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        string html = await GetAsync($"/ua/scopes/new?parentId={scenario.North}");

        Assert.Contains("New scope", html, StringComparison.Ordinal);
        Assert.Contains(ScopeTexts.Name.Label.Default, html, StringComparison.Ordinal);

        string name = $"created-{Guid.NewGuid():N}"[..16];
        using IServiceScope scope = factory.Services.CreateScope();
        ViewSendResult sent = await model.SubmitFormAsync(
            scope.ServiceProvider.GetRequiredService<ISerginDispatcher>(),
            id: null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(CreateScopeCommand.ParentId)] = scenario.North,
                [nameof(CreateScopeCommand.Name)] = name,
            },
            version: null);

        Assert.False(sent.IsError, sent.IsError ? sent.Errors[0].Description : string.Empty);
        Guid created = Assert.IsType<CreateScopeCommandResponse>(sent.Value).Id;
        Assert.Equal($"/ua/scopes/{created}", model.Routes.Detail!(created));
        Assert.Equal(scenario.North, (await LoadScopeAsync(created)).ParentId);
    }

    private AggregateViewModel Model() => factory.Services.GetRequiredService<AggregateViewRegistry>().For<Scope>();

    private async Task<ScopeQueryResponse> LoadScopeAsync(Guid scope)
    {
        using IServiceScope services = factory.Services.CreateScope();
        ErrorOr<Versioned<ScopeQueryResponse>> loaded = await services.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendVersionedAsync(new GetScopeByIdQueryCommand(new ScopeId(scope)));

        Assert.False(loaded.IsError, loaded.IsError ? loaded.FirstError.Description : string.Empty);
        return loaded.Value.Value;
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string Html(HtmlRootComponent root) => WebUtility.HtmlDecode(root.ToHtmlString());

    // Renders the generated scope detail view in process as the development user, or as the caller, under a host that
    // records where the view leaves to and how often it told its host the record changed.
    private async Task RenderDetailAsync(
        Guid scopeId, TestCaller? caller, Func<HtmlRootComponent, SerginGeneratedDetailView<Scope>, RecordingHost, Task> test)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/ua/scopes/{scopeId}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var viewHost = new RecordingHost();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ScopeDetailHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<ScopeDetailHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(ScopeDetailHost.Id)] = scopeId,
                    [nameof(ScopeDetailHost.ViewHost)] = viewHost.Host,
                }));

            await test(root, ScopeDetailHost.View!, viewHost);
        });
    }

    /// <summary>A view host that records where the view leaves to and how often it was told the record changed.</summary>
    private sealed class RecordingHost
    {
        private readonly List<string> leaves = [];

        public RecordingHost() => Host = new SerginViewHost(
            SerginViewHostKind.Page,
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

    /// <summary>The scope's generated detail view under a given host, with the providers its dialogs render into.</summary>
    private sealed class ScopeDetailHost : ComponentBase
    {
        public static SerginGeneratedDetailView<Scope>? View { get; set; }

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
                content.OpenComponent<SerginGeneratedDetailView<Scope>>(0);
                content.AddComponentParameter(1, nameof(SerginGeneratedDetailView<Scope>.Id), Id);
                content.AddComponentReferenceCapture(2, instance => View = (SerginGeneratedDetailView<Scope>)instance);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
