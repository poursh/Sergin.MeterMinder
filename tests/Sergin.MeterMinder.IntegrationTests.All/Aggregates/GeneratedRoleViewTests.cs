using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Runtime;
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
using Sergin.Administration.UserAccess.Application.Configurations.Roles;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Delete;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.GetOne;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Rename;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.SetInheritable;
using Sergin.Administration.UserAccess.Domain.Roles;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.Administration.UserAccess.Presentation.Blazor.Roles;
using Sergin.Administration.UserAccess.Presentation.Blazor.Roles.Components;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Validation;
using Sergin.SharedKernel.Presentation.Blazor.Views;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.GetList;
using Sergin.Administration.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated Role views show: the trail through the owner scope and its link, rename and inheritability edited
/// inline (a switch only for someone who may edit), a delete that asks whether to cascade and leaves for the owner scope,
/// the granted-permissions slot sending at the held version, and the create form taking its owner scope from the query.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class GeneratedRoleViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task RoleDetail_TrailIsScopesOwnerRole_AndOwnedByLinksTheScope()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        string roleName = $"trail-{Guid.NewGuid():N}"[..16];
        Guid role = await scenario.CreateRoleAsync(scenario.North, roleName, inheritable: false, RbacScenario.Read);

        BelongsToView belongsTo = Assert.IsType<BelongsToView>(Model().BelongsTo);
        Assert.Equal(typeof(Scope), belongsTo.Parent);

        string html = await GetAsync($"/ua/roles/{role}");

        // The list step, the owner as stored (a link), then the role: in that order.
        int scopes = html.IndexOf("href=\"/ua/scopes\"", StringComparison.Ordinal);
        int owner = html.IndexOf($"href=\"/ua/scopes/{scenario.North}\"", StringComparison.Ordinal);
        int current = html.IndexOf(roleName, StringComparison.Ordinal);
        Assert.True(scopes >= 0 && owner > scopes && current > owner, $"expected scopes, owner, role in order ({scopes}, {owner}, {current})");

        // The detail's own "Owned by" field links the scope again, by its name.
        Assert.Contains(">Owned by<", html, StringComparison.Ordinal);
        Assert.Contains("North Region", html, StringComparison.Ordinal);
        Assert.True(CountOf(html, $"href=\"/ua/scopes/{scenario.North}\"") >= 2);
    }

    [Fact]
    public async Task RoleDetail_RenameIsInlineCard_InheritableIsInlineSwitch()
    {
        AggregateViewModel model = Model();

        Assert.Equal(SerginEditMode.Inline, model.Commands.Single(command => command.Request == typeof(RenameRoleCommand)).EditMode);
        Assert.Equal(SerginEditMode.Inline, model.Commands.Single(command => command.Request == typeof(SetRoleInheritableCommand)).EditMode);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"inline-{Guid.NewGuid():N}"[..16], inheritable: true, RbacScenario.Read);

        string html = await GetAsync($"/ua/roles/{role}");

        Assert.Contains(">Inheritable<", html, StringComparison.Ordinal);
        Assert.Contains("mud-switch", html, StringComparison.Ordinal);
        Assert.Contains(">Rename<", html, StringComparison.Ordinal);

        // A rename sent through the inline seam is stored.
        CommandView rename = model.Commands.Single(command => command.Request == typeof(RenameRoleCommand));
        string renamed = $"renamed-{Guid.NewGuid():N}"[..16];
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
            ViewSendResult loaded = await model.LoadRecordAsync(dispatcher, role);
            var setProperty = new GeneratedSetProperty(scope.ServiceProvider.GetRequiredService<ISerginFormValidator>());

            SetPropertyOutcome outcome = await setProperty.SendAsync(dispatcher, rename, role, renamed, loaded.Version);

            Assert.Equal(SetPropertyStatus.Sent, outcome.Status);
        }

        Assert.Equal(renamed, (await LoadRoleAsync(role)).Name);
        Assert.DoesNotContain("Scopes below the owner can use it too", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleDetail_ForAReader_ShowsInheritableAsText()
    {
        CommandView setInheritable = Model().Commands.Single(command => command.Request == typeof(SetRoleInheritableCommand));
        Assert.Equal(RolePermissions.Manage, Assert.Single(setInheritable.Permissions));

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"reader-{Guid.NewGuid():N}"[..16], inheritable: true, RbacScenario.Read);
        TestCaller reader = await scenario.CreateGrantedCallerAsync(Scope.RootId.Value, RolePermissions.Read.Permission);

        await RenderDetailAsync(role, reader, (root, _, _) =>
        {
            string html = Html(root);
            Assert.Contains(">Inheritable<", html, StringComparison.Ordinal);
            Assert.Contains(">Yes<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("mud-switch", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Rename<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">Delete<", html, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RoleDetail_Delete_AsksForCascade_AndLeavesToTheOwnerScope()
    {
        AggregateViewModel model = Model();
        CommandView delete = model.Commands.Single(command => command.Request == typeof(DeleteRoleCommand));

        FormFieldView cascade = Assert.Single(delete.ConfirmFields);
        Assert.Equal(nameof(DeleteRoleCommand.Cascade), cascade.Name);
        Assert.Equal(RoleTexts.Cascade, cascade.Label);
        Assert.Equal(RoleUiTexts.DeleteConfirm, delete.ConfirmText);
        Assert.Null(delete.BuildFromId);

        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"delete-{Guid.NewGuid():N}"[..16], inheritable: false, RbacScenario.Read);

        await RenderDetailAsync(role, caller: null, async (root, view, viewHost) =>
        {
            IDialogReference asking = await view.AskValuesAsync(delete);
            var dialog = (SerginConfirmValuesDialog)asking.Dialog!;
            string html = Html(root);
            Assert.Contains(RoleTexts.Cascade.Label.Default, html, StringComparison.Ordinal);
            Assert.Contains("It can no longer be assigned.", html, StringComparison.Ordinal);

            dialog.Confirm();
            DialogResult answered = (await asking.Result)!;
            Assert.False(answered.Canceled);

            await view.DeleteWithValuesAsync(delete, (IReadOnlyDictionary<string, object?>)answered.Data!);

            Assert.Equal([$"/ua/scopes/{scenario.North}"], viewHost.Leaves);
        });

        Assert.False(await RoleExistsAsync(role));
    }

    [Fact]
    public async Task RoleDelete_WithCascade_RevokesTheAssignment()
    {
        (RbacScenario scenario, Guid role, Guid user) = await AssignedRoleAsync();
        CommandView delete = Model().Commands.Single(command => command.Request == typeof(DeleteRoleCommand));

        await RenderDetailAsync(role, caller: null, async (_, view, viewHost) =>
        {
            await view.DeleteWithValuesAsync(delete, CascadeValues(cascade: true));

            Assert.Equal([$"/ua/scopes/{scenario.North}"], viewHost.Leaves);
        });

        Assert.False(await RoleExistsAsync(role));
        IReadOnlyCollection<UserAssignmentItem> assignments = await scenario.SendAsync(new GetUserAssignmentsQueryCommand(new UserInternalId(user)));
        Assert.All(assignments.Where(assignment => assignment.RoleId == role), assignment => Assert.NotNull(assignment.RevokedAtUtc));
    }

    [Fact]
    public async Task RoleDelete_WithoutCascade_IsRefusedWhileAssigned_AndTheRoleStays()
    {
        (RbacScenario scenario, Guid role, Guid user) = await AssignedRoleAsync();
        CommandView delete = Model().Commands.Single(command => command.Request == typeof(DeleteRoleCommand));

        List<Error> notified = [];
        await RenderDetailAsync(role, caller: null, async (_, view, viewHost) =>
        {
            await view.DeleteWithValuesAsync(delete, CascadeValues(cascade: false));

            Assert.Empty(viewHost.Leaves);
        }, notified);

        Assert.Single(notified);

        Assert.True(await RoleExistsAsync(role));
        IReadOnlyCollection<UserAssignmentItem> assignments = await scenario.SendAsync(new GetUserAssignmentsQueryCommand(new UserInternalId(user)));
        Assert.All(assignments.Where(assignment => assignment.RoleId == role), assignment => Assert.Null(assignment.RevokedAtUtc));
    }

    private static Dictionary<string, object?> CascadeValues(bool cascade) =>
        new(StringComparer.Ordinal) { [nameof(DeleteRoleCommand.Cascade)] = cascade };

    private async Task<(RbacScenario Scenario, Guid Role, Guid User)> AssignedRoleAsync()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"held-{Guid.NewGuid():N}"[..16], inheritable: false, RbacScenario.Read);
        Guid user = await scenario.CreateUserAsync("role-holder");
        ErrorOr<AssignRoleCommandResponse> assigned = await scenario.AssignAsync(user, role, scenario.North, includeSubtree: false);
        Assert.False(assigned.IsError, assigned.IsError ? assigned.FirstError.Description : string.Empty);

        return (scenario, role, user);
    }

    private async Task<bool> RoleExistsAsync(Guid role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ErrorOr<Versioned<RoleQueryResponse>> loaded = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendVersionedAsync(new GetRoleByIdQueryCommand(new RoleId(role)));

        return !loaded.IsError;
    }

    [Fact]
    public async Task RolePermissions_GrantAndRevoke_AtTheHeldVersion()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"perms-{Guid.NewGuid():N}"[..16], inheritable: false, RbacScenario.Read);
        string granted = RbacScenario.Update.Value;

        // Each send is at the version its view held; the real view hands a fresh context after its reload.
        await RenderSlotAsync(role, async (slot, reloads, notified, _) =>
        {
            await slot.GrantAsync(granted);

            Assert.Empty(notified);
            Assert.Equal(1, reloads());
            Assert.Contains(granted, (await LoadRoleAsync(role)).Permissions);
        });

        await RenderSlotAsync(role, async (slot, reloads, notified, _) =>
        {
            await slot.RevokeAsync(granted);

            Assert.Empty(notified);
            Assert.Equal(1, reloads());
            Assert.DoesNotContain(granted, (await LoadRoleAsync(role)).Permissions);
        });
    }

    [Fact]
    public async Task RolePermissions_StaleGrant_IsRefusedAndReloads()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, $"stale-{Guid.NewGuid():N}"[..16], inheritable: false, RbacScenario.Read);

        await RenderSlotAsync(role, async (slot, reloads, notified, _) =>
        {
            // Someone else changes the role after the view loaded it: the held version is stale.
            ErrorOr<SetRoleInheritableCommandResponse> moved = await scenario.SendAtRoleVersionAsync(
                role, new SetRoleInheritableCommand(new RoleId(role), true));
            Assert.False(moved.IsError, moved.IsError ? moved.FirstError.Description : string.Empty);

            await slot.GrantAsync(RbacScenario.Update.Value);

            // Refused once, nothing granted, and the view asked to reload.
            Error shown = Assert.Single(notified);
            Assert.True(VersionErrors.IsStale(shown));
            Assert.Equal(1, reloads());
            Assert.DoesNotContain(RbacScenario.Update.Value, (await LoadRoleAsync(role)).Permissions);
        });
    }

    [Fact]
    public async Task RoleCreate_TakesScopeIdFromTheQuery_AndPicksPermissions()
    {
        AggregateViewModel model = Model();

        Assert.Equal(nameof(CreateRoleCommand.OwnerScopeId), model.CreateParent);
        Assert.Equal(RoleUiTexts.New, model.CreateTitle);
        FormFieldView permissions = Assert.Single(model.CreateFields, field => field.Name == nameof(CreateRoleCommand.Permissions));
        Assert.Equal(FormInputKind.Component, permissions.Kind);
        Assert.Equal(typeof(PermissionPickerInput), permissions.Component);
        FormFieldView inheritable = Assert.Single(model.CreateFields, field => field.Name == nameof(CreateRoleCommand.IsInheritable));
        Assert.Equal(RoleUiTexts.InheritableOnCreate, inheritable.Label.Label);
        Assert.DoesNotContain(model.CreateFields, field => field.Name == nameof(CreateRoleCommand.OwnerScopeId));

        string html = await GetAsync($"/ua/roles/new?scopeId={Scope.RootId.Value}");

        Assert.Contains(RoleUiTexts.InheritableOnCreate.Default, html, StringComparison.Ordinal);
        Assert.Contains(">Permissions<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mud-list-subheader", html, StringComparison.Ordinal);

        // The form's values, with the query's scope as the parent, build the command; the picked permissions are sent.
        string name = $"created-{Guid.NewGuid():N}"[..16];
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        IReadOnlyCollection<Permission> picked = [RbacScenario.Read];

        ViewSendResult sent = await model.SubmitFormAsync(
            dispatcher,
            id: null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(CreateRoleCommand.OwnerScopeId)] = Scope.RootId.Value,
                [nameof(CreateRoleCommand.Name)] = name,
                [nameof(CreateRoleCommand.IsInheritable)] = true,
                [nameof(CreateRoleCommand.Permissions)] = picked,
            },
            version: null);

        Assert.False(sent.IsError, sent.IsError ? sent.Errors[0].Description : string.Empty);
        Guid created = Assert.IsType<CreateRoleCommandResponse>(sent.Value).Id;
        Assert.Equal($"/ua/roles/{created}", model.Routes.Detail!(created));

        RoleQueryResponse role = await LoadRoleAsync(created);
        Assert.Equal(Scope.RootId.Value, role.OwnerScopeId);
        Assert.Equal(name, role.Name);
        Assert.True(role.IsInheritable);
        Assert.Equal([RbacScenario.Read.Value], role.Permissions);
    }

    [Fact]
    public async Task RoleCreate_WithoutTouchingThePicker_CreatesARoleWithNoPermissions()
    {
        AggregateViewModel model = Model();
        string name = $"bare-{Guid.NewGuid():N}"[..16];
        using IServiceScope scope = factory.Services.CreateScope();

        // The picker only reports after a pick, so an untouched one leaves Permissions out of the form's values.
        ViewSendResult sent = await model.SubmitFormAsync(
            scope.ServiceProvider.GetRequiredService<ISerginDispatcher>(),
            id: null,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(CreateRoleCommand.OwnerScopeId)] = Scope.RootId.Value,
                [nameof(CreateRoleCommand.Name)] = name,
            },
            version: null);

        Assert.False(sent.IsError, sent.IsError ? sent.Errors[0].Description : string.Empty);
        RoleQueryResponse role = await LoadRoleAsync(Assert.IsType<CreateRoleCommandResponse>(sent.Value).Id);
        Assert.Equal(name, role.Name);
        Assert.Empty(role.Permissions);
    }

    private AggregateViewModel Model() => factory.Services.GetRequiredService<AggregateViewRegistry>().For<Role>();

    private async Task<RoleQueryResponse> LoadRoleAsync(Guid role)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ErrorOr<Versioned<RoleQueryResponse>> loaded = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendVersionedAsync(new GetRoleByIdQueryCommand(new RoleId(role)));

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

    private static int CountOf(string text, string value)
    {
        int count = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // Renders the generated role detail view in process as the development user, or as <paramref name="caller"/>, under a
    // host that records where the view leaves to, with the providers its dialogs render into.
    private async Task RenderDetailAsync(
        Guid role, TestCaller? caller, Func<HtmlRootComponent, SerginGeneratedDetailView<Role>, RecordingHost, Task> test,
        List<Error>? notified = null)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/ua/roles/{role}");
        IServiceProvider services = notified is null
            ? scope.ServiceProvider
            : new OverridingProvider(scope.ServiceProvider, new RecordingPresenter(scope.ServiceProvider.GetRequiredService<IUiErrorPresenter>(), notified));
        await using var renderer = new HtmlRenderer(services, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var viewHost = new RecordingHost();

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RoleDetailHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<RoleDetailHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(RoleDetailHost.Id)] = role,
                    [nameof(RoleDetailHost.ViewHost)] = viewHost.Host,
                }));

            await test(root, RoleDetailHost.View!, viewHost);
        });
    }

    // Renders the permissions slot alone, given the role as loaded now and its held version, and a recording error
    // presenter; the test reads how many times the slot asked its view to reload and which errors it showed.
    private async Task RenderSlotAsync(
        Guid role, Func<RolePermissionsSlot, Func<int>, IReadOnlyList<Error>, HtmlRootComponent, Task> test)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/ua/roles/{role}");
        ErrorOr<Versioned<RoleQueryResponse>> loaded = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendVersionedAsync(new GetRoleByIdQueryCommand(new RoleId(role)));
        Assert.False(loaded.IsError);
        RoleQueryResponse record = loaded.Value.Value;
        RowVersion version = loaded.Value.Version;

        List<Error> notified = [];
        int reloads = 0;
        var context = new AggregateViewContext(
            record,
            version,
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            },
            [role]);
        var services = new OverridingProvider(
            scope.ServiceProvider, new RecordingPresenter(scope.ServiceProvider.GetRequiredService<IUiErrorPresenter>(), notified));

        await using var renderer = new HtmlRenderer(services, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            SlotHost.Slot = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<SlotHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(SlotHost.Context)] = context }));

            await test(SlotHost.Slot!, () => reloads, notified, root);
        });
    }

    /// <summary>A view host that records where the view leaves to.</summary>
    private sealed class RecordingHost
    {
        private readonly List<string> leaves = [];

        public RecordingHost() => Host = new SerginViewHost(
            SerginViewHostKind.Page,
            href =>
            {
                leaves.Add(href);
                return Task.CompletedTask;
            });

        public SerginViewHost Host { get; }

        public IReadOnlyList<string> Leaves => leaves;
    }

    private sealed class OverridingProvider(IServiceProvider inner, IUiErrorPresenter presenter) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IUiErrorPresenter) ? presenter : inner.GetService(serviceType);
    }

    private sealed class RecordingPresenter(IUiErrorPresenter inner, List<Error> notified) : IUiErrorPresenter
    {
        public SerginProblem Present(Error error) => inner.Present(error);

        public void Notify(Error error) => notified.Add(error);

        public void Notify(IReadOnlyList<Error> errors) => notified.AddRange(errors);
    }

    /// <summary>The role's generated detail view under a given host, with the providers its dialogs render into.</summary>
    private sealed class RoleDetailHost : ComponentBase
    {
        public static SerginGeneratedDetailView<Role>? View { get; set; }

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
                content.OpenComponent<SerginGeneratedDetailView<Role>>(0);
                content.AddComponentParameter(1, nameof(SerginGeneratedDetailView<Role>.Id), Id);
                content.AddComponentReferenceCapture(2, instance => View = (SerginGeneratedDetailView<Role>)instance);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>The permissions slot alone, with a handle on it.</summary>
    private sealed class SlotHost : ComponentBase
    {
        public static RolePermissionsSlot? Slot { get; set; }

        [Parameter]
        public AggregateViewContext Context { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<RolePermissionsSlot>(1);
            builder.AddComponentParameter(2, nameof(RolePermissionsSlot.Context), Context);
            builder.AddComponentReferenceCapture(3, instance => Slot = (RolePermissionsSlot)instance);
            builder.CloseComponent();
        }
    }
}
