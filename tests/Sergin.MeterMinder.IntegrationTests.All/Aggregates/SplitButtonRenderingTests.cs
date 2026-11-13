using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// The split button on the action surfaces: a command with at least one visible menu item draws a drop-down beside
/// it, a command whose items are all hidden is the plain button, and a hidden command hides its menu too.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SplitButtonRenderingTests(SerginWebApiFactory<Program> factory)
{
    private const string MoreActions = "More actions";
    private const string ReadProfile = "Read profile";

    private static readonly PermissionDefinition Held = DevicePermissions.Read;

    [Fact]
    public async Task ActionBar_CommandWithVisibleMenuItem_RendersSplitButton()
    {
        string html = await RenderAsync<SerginActionBar>(
            [Held],
            Parameters(nameof(SerginActionBar.Actions), new[] { Action(Held, Item(Held)) }));

        Assert.Contains(ReadProfile, html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, $"aria-label=\"{MoreActions}\""));
    }

    [Fact]
    public async Task ActionBar_CommandWithHiddenMenuItems_RendersPlainButton()
    {
        string html = await RenderAsync<SerginActionBar>(
            [Held],
            Parameters(nameof(SerginActionBar.Actions), new[] { Action(Held, Item(ScheduledTaskPermissions.Manage)) }));

        Assert.Contains(ReadProfile, html, StringComparison.Ordinal);
        Assert.DoesNotContain(MoreActions, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionBar_CommandHidden_HidesItsMenuToo()
    {
        string html = await RenderAsync<SerginActionBar>(
            [],
            Parameters(nameof(SerginActionBar.Actions), new[] { Action(Held, Item(Held)) }));

        Assert.DoesNotContain(ReadProfile, html, StringComparison.Ordinal);
        Assert.DoesNotContain(MoreActions, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectionBar_BulkActionWithMenuItem_RendersSplitButton()
    {
        string html = await RenderAsync<SerginSelectionBar>(
            [Held],
            new Dictionary<string, object?>
            {
                [nameof(SerginSelectionBar.Count)] = 3,
                [nameof(SerginSelectionBar.Actions)] = new[] { Action(Held, Item(Held)) },
            });

        Assert.Contains(MoreActions, html, StringComparison.Ordinal);
    }

    // MudBlazor mounts a popover's content through JS, so a static render shows the menu's button but never its
    // entries; the entries a row menu lists are what the visibility rule returns, which is what is pinned here.
    [Fact]
    public async Task ActionMenu_RendersForAVisibleAction_AndItsItemsAreFilteredByTheSameRule()
    {
        PermissionDefinition manage = ScheduledTaskPermissions.Manage;
        SerginPageAction action = Action(Held, Item(Held)) with { MenuItems = [Item(Held), Item(manage)] };

        string html = await RenderAsync<SerginActionMenu>(
            [Held],
            Parameters(nameof(SerginActionMenu.Actions), new[] { action }));

        Assert.Contains("mud-menu", html, StringComparison.Ordinal);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current =
            await tree.CreateGrantedCallerAsync(Scope.RootId.Value, Held.Permission);
        var visibility = new SerginActionVisibility(scope.ServiceProvider.GetRequiredService<IScopeAuthorization>());

        IReadOnlyList<SerginPageAction> visible = await visibility.VisibleAsync([action]);

        SerginMenuItem only = Assert.Single(Assert.Single(visible).MenuItems!);
        Assert.Equal(SplitTexts.Item, only.Label);
    }

    private static Dictionary<string, object?> Parameters(string name, object value) => new() { [name] = value };

    private static SerginPageAction Action(PermissionDefinition permission, SerginMenuItem item) =>
        new(SplitTexts.Command, null, SerginActionKind.Secondary, Permission: permission, MenuItems: [item]);

    private static SerginMenuItem Item(PermissionDefinition permission) =>
        new(SplitTexts.Item, null, default, permission);

    private async Task<string> RenderAsync<TComponent>(
        PermissionDefinition[] granted,
        Dictionary<string, object?> parameters,
        Func<Task>? afterRender = null)
        where TComponent : IComponent
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller caller = await tree.CreateGrantedCallerAsync(
            Scope.RootId.Value, [.. granted.Select(permission => permission.Permission)]);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        ((IHostEnvironmentNavigationManager)scope.ServiceProvider.GetRequiredService<NavigationManager>())
            .Initialize("http://localhost/", "http://localhost/");

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
        {
            HtmlRootComponent root = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));

            if (afterRender is not null)
            {
                await afterRender();
            }

            return root.ToHtmlString();
        }));
    }

    private static int Count(string html, string needle)
    {
        int count = 0;

        for (int at = html.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = html.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

internal static class SplitTexts
{
    private static readonly TextCategory category = TextCategory.Create("Tst", "SplitButton");

    public static TextDefinition Command { get; } = category.Text("Command", "Read profile");

    public static TextDefinition Item { get; } = category.Text("Item", "Schedule item");
}
