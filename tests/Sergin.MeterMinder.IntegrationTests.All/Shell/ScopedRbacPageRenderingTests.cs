using System.Net;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.UserAccess.Domain.Scopes;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.UserAccess.Application.Configurations.Scopes;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The scope and role pages prerender with their data: the seeded root scope and the seeded viewer role are
/// always there, so these need no setup.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopedRbacPageRenderingTests(SerginWebApiFactory<Program> factory)
{
    private const string ViewerRoleId = "01920000-0000-7000-8000-0000000000a2";

    [Fact]
    public async Task ScopeTree_ListsTheRoot()
    {
        string html = await GetAsync("/ua/scopes");

        Assert.Contains($"href=\"/ua/scopes/{Scope.RootId.Value}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootScopeDetail_ShowsTheGlobalRolesUsableThere()
    {
        string html = await GetAsync($"/ua/scopes/{Scope.RootId.Value}");

        Assert.Contains("The root of the tree", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/roles/{ViewerRoleId}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeDetail_LinksToItsDescendantsOnly()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.North}");

        Assert.Contains($"href=\"/ua/scopes/{scenario.Feeder1}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/scopes/{scenario.Feeder2}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"href=\"/ua/scopes/{scenario.South}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeafScopeDetail_SaysItHasNoChildren()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);

        string html = await GetAsync($"/ua/scopes/{scenario.Feeder1}");

        Assert.Contains("No child scopes.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewerRoleDetail_ShowsItsPermissions()
    {
        string html = await GetAsync($"/ua/roles/{ViewerRoleId}");

        Assert.Contains(DevicePermissions.Read.Label, html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ua/scopes/{Scope.RootId.Value}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleDetailPage_ShowsAGrantedPermissionByItsLabel()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await scenario.CreateRoleAsync(scenario.North, "scope-keeper", inheritable: false, ScopePermissions.Manage.Permission);

        string html = await GetAsync($"/ua/roles/{role}");

        Assert.Contains("Create, rename, move and delete scopes", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleDetailPage_ListsAGrantMissingFromTheCatalogue()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await new Permissions.UserAccessPermissionCatalogTests(factory).SeedRoleWithUnknownPermissionAsync(scenario);

        string html = await GetAsync($"/ua/roles/{role}");

        Assert.Contains("Unknown", html, StringComparison.Ordinal);
        Assert.Contains("permission.ua.nothing.here", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleDetailPage_ListsAMalformedStoredGrantAsUnknown_InsteadOfFailingTheRender()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        Guid role = await new Permissions.UserAccessPermissionCatalogTests(factory).SeedRoleWithMalformedPermissionAsync(scenario, "Not A Permission!");

        string html = await GetAsync($"/ua/roles/{role}");

        Assert.Contains("Unknown", html, StringComparison.Ordinal);
        Assert.Contains("Not A Permission!", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/ua/scopes/new")]
    [InlineData("/ua/roles/new?scopeId=01920000-0000-7000-8000-0000000000b1")]
    public async Task CreatePages_Render(string path)
    {
        string html = await GetAsync(path);

        Assert.Contains("Create", html, StringComparison.Ordinal);
    }

    private async Task<string> GetAsync(string path)
    {
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
