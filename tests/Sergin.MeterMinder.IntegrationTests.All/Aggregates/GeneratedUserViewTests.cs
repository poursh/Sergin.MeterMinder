using System.Net;
using System.Text.RegularExpressions;
using ErrorOr;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Sergin.Administration.UserAccess.Application.Configurations.Users.Assignments;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.Create;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.DeactivateUser;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.GetList;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.GetOne;
using Sergin.Administration.UserAccess.Domain.Users;
using Sergin.Administration.UserAccess.Domain.Users.Assignments;
using Sergin.Administration.UserAccess.Presentation.Blazor.Users;
using Sergin.Administration.UserAccess.Presentation.Blazor.Users.Assignments.Components;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Views.Parts;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.Views;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the generated User views show: the list's name and identifier columns with no search box (its repository reads
/// paging only), the detail's identifier and a Deactivate that asks first, the assignments drawn by their own section
/// component, which reloads with the header's Reload, and the create form that opens the new user.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed partial class GeneratedUserViewTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task UserList_RendersNameAndIdentifierColumns_AndNoSearch()
    {
        AggregateViewModel model = Model();

        FieldView name = Assert.Single(model.ListFields, field => field.Name == nameof(GetUserListItem.UserName));
        FieldView id = Assert.Single(model.ListFields, field => field.Name == nameof(GetUserListItem.Id));
        Assert.False(name.Hidden);
        Assert.False(id.Hidden);
        Assert.Equal(UserUiTexts.Identifier, id.Label);
        Assert.Null(model.ListFieldSet);

        // The name column comes first and carries the row link; the id column is plain text.
        IReadOnlyList<ListColumn> columns = GeneratedViewSupport.ColumnsOf(
            model.ListFields, model.ListFieldSet, GeneratedViewSupport.TitleFieldOf(model.ListFields, model.ListTitleField));
        Assert.Equal([nameof(GetUserListItem.UserName), nameof(GetUserListItem.Id)], columns.Select(column => column.Field.Name));
        Assert.Equal([true, false], columns.Select(column => column.Links));

        string html = await GetAsync("/ua/users");

        int nameHeader = html.IndexOf(">User name<", StringComparison.Ordinal);
        int idHeader = html.IndexOf(">Id<", StringComparison.Ordinal);
        Assert.True(nameHeader >= 0 && idHeader > nameHeader, $"expected the User name header before the Id header ({nameHeader}, {idHeader})");
        Assert.DoesNotMatch("class=\"[^\"]*sergin-search", html);
        Assert.DoesNotContain("aria-label=\"Quick search\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserDetail_ShowsIdentifier_AndDeactivateAsksFirst()
    {
        string userName = $"generated-detail-{Guid.CreateVersion7()}";
        Guid user = await CreateUserAsync(userName);
        AggregateViewModel model = Model();

        string html = await GetAsync($"/ua/users/{user}");

        Assert.Contains(userName, html, StringComparison.Ordinal);
        int nameLabel = html.IndexOf(">User name<", StringComparison.Ordinal);
        int idLabel = html.IndexOf(">Id<", StringComparison.Ordinal);
        Assert.True(nameLabel >= 0 && idLabel > nameLabel, $"expected the User name label before the Id label ({nameLabel}, {idLabel})");
        Assert.Contains(user.ToString(), html, StringComparison.Ordinal);
        Assert.Contains(">Deactivate", html, StringComparison.Ordinal);

        CommandView deactivate = Assert.Single(model.Commands, command => command.Request == typeof(DeactivateUserCommand));
        Assert.Equal(SerginActionKind.Danger, deactivate.Kind);
        Assert.Equal(UserUiTexts.DeactivateTitle, deactivate.ConfirmTitle);
        Assert.Equal(UserUiTexts.DeactivateConfirm, deactivate.ConfirmText);
        Assert.Equal(UserUiTexts.Deactivate, deactivate.ConfirmLabel);

        // The confirm's text fills {UserName} from the loaded record.
        var record = new UserQueryResponse(user, userName);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IStringLocalizer localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer>();
            Assert.Equal(
                $"Deactivate {userName}? They can no longer sign in.",
                localizer.Text(UserUiTexts.DeactivateConfirm, model.DetailArguments!(record)));
        }

        // Through the actions seam: declined sends nothing, accepted sends once, each time asked about the record.
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

        SerginPageAction action = actions.For(model, user, _ => [], record: record).Single(candidate => candidate.Label == UserUiTexts.Deactivate);

        await action.OnClick.InvokeAsync();
        Assert.Equal(0, runs);

        answer = true;
        await action.OnClick.InvokeAsync();
        Assert.Equal(1, runs);

        Assert.Equal(2, asked.Count);
        Assert.All(asked, ask =>
        {
            Assert.Same(deactivate, ask.Command);
            Assert.Same(record, ask.Record);
        });
    }

    [Fact]
    public async Task UserDetail_EmbedsAssignmentsSection()
    {
        ChildViewModel assignments = Assert.Single(Model().Children, child => child.Child == typeof(RoleAssignment));
        Assert.Equal(typeof(UserAssignmentsSection), assignments.Section);
        Assert.Equal(AssignmentTexts.Singular, assignments.Singular);

        Guid user = await CreateUserAsync($"generated-section-{Guid.CreateVersion7()}");

        string html = await GetAsync($"/ua/users/{user}");

        Assert.Matches("<section class=\"sergin-view sergin-entity-list-section[^\"]*\" aria-label=\"Assignments\"", html);
        Assert.Matches("<section class=\"sergin-view sergin-entity-list-section[^\"]*\" aria-label=\"Effective permissions\"", html);
        Assert.Contains("No assignments yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserDetail_HeaderReload_ReloadsAssignments()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        string roleName = $"reloaded-{Guid.NewGuid():N}"[..20];
        Guid role = await scenario.CreateRoleAsync(scenario.North, roleName, inheritable: false, RbacScenario.Read);
        Guid user = await scenario.CreateUserAsync("generated-reload");

        await RenderDetailAsync(user, async (root, view) =>
        {
            Assert.DoesNotContain(roleName, Html(root), StringComparison.Ordinal);

            // Assigned elsewhere after the view read the user: the header's Reload shows the new row.
            ErrorOr<AssignRoleCommandResponse> assigned = await scenario.AssignAsync(user, role, scenario.North, includeSubtree: false);
            Assert.False(assigned.IsError, assigned.IsError ? assigned.FirstError.Description : string.Empty);

            int generation = view.Generation;

            await view.HeaderReloadAsync();

            Assert.Equal(generation + 1, view.Generation);

            // The section reads its rows in its own parameters-set, which the view's reload does not await.
            Assert.Contains(roleName, await HtmlOnceAsync(root, roleName), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task UserCreate_TitleIsNewUser_AndSaveOpensTheUser()
    {
        AggregateViewModel model = Model();

        string html = await GetAsync("/ua/users/new");

        Assert.Equal("New user", PageTitle().Match(html).Groups[1].Value.Trim());
        Assert.Equal(UserUiTexts.New, model.CreateTitle);
        Assert.Contains(">User name<", html, StringComparison.Ordinal);

        // The form's values build CreateUserCommand, and its created id is what the form leaves to.
        string userName = $"generated-create-{Guid.CreateVersion7()}";
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ViewSendResult sent = await model.SubmitFormAsync(
            dispatcher, id: null, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(CreateUserCommand.UserName)] = userName }, version: null);

        Assert.False(sent.IsError, sent.IsError ? sent.Errors[0].Description : string.Empty);
        Guid created = Assert.IsType<CreateUserCommandResponse>(sent.Value).Id;
        Assert.Equal($"/ua/users/{created}", model.Routes.Detail!(created));
        Assert.Contains(userName, await GetAsync($"/ua/users/{created}"), StringComparison.Ordinal);
    }

    private AggregateViewModel Model() => factory.Services.GetRequiredService<AggregateViewRegistry>().For<User>();

    private async Task<Guid> CreateUserAsync(string userName)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ErrorOr<CreateUserCommandResponse> created = await scope.ServiceProvider.GetRequiredService<ISerginDispatcher>()
            .SendAsync(new CreateUserCommand(new UserName(userName)));

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);
        return created.Value.Id;
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string Html(HtmlRootComponent root) => WebUtility.HtmlDecode(root.ToHtmlString());

    // The rendered HTML once it holds the text, or after five seconds; awaited on the renderer's dispatcher, so the
    // components' pending loads run in between.
    private static async Task<string> HtmlOnceAsync(HtmlRootComponent root, string text)
    {
        for (int attempt = 0; attempt < 50 && !Html(root).Contains(text, StringComparison.Ordinal); attempt++)
        {
            await Task.Delay(100);
        }

        return Html(root);
    }

    // Renders the generated user detail view in process as the development user, with the providers its dialogs render
    // into, and hands the test the root and the view on the renderer's dispatcher.
    private async Task RenderDetailAsync(Guid user, Func<HtmlRootComponent, SerginGeneratedDetailView<User>, Task> test)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", $"http://localhost/ua/users/{user}");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            UserDetailHost.View = null;
            HtmlRootComponent root = await renderer.RenderComponentAsync<UserDetailHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(UserDetailHost.Id)] = user }));

            await test(root, UserDetailHost.View!);
        });
    }

    [GeneratedRegex("<title>([^<]*)</title>")]
    private static partial Regex PageTitle();

    /// <summary>Runs every event callback it receives, as a component's dispatcher does.</summary>
    private sealed class InvokingReceiver : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
    }

    /// <summary>The user's generated detail view, embedded, with the providers its dialogs render into.</summary>
    private sealed class UserDetailHost : ComponentBase
    {
        public static SerginGeneratedDetailView<User>? View { get; set; }

        [Parameter]
        public Guid Id { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<CascadingValue<SerginViewHost>>(2);
            builder.AddComponentParameter(3, nameof(CascadingValue<SerginViewHost>.Value), SerginViewHost.Unhosted);
            builder.AddComponentParameter(4, nameof(CascadingValue<SerginViewHost>.ChildContent), (RenderFragment)(content =>
            {
                content.OpenComponent<SerginGeneratedDetailView<User>>(0);
                content.AddComponentParameter(1, nameof(SerginGeneratedDetailView<User>.Id), Id);
                content.AddComponentReferenceCapture(2, instance => View = (SerginGeneratedDetailView<User>)instance);
                content.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
