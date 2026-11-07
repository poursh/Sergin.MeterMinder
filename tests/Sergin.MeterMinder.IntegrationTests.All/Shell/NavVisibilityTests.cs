using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.Administration.UserAccess.Application.Configurations.Scopes;
using Sergin.Administration.UserAccess.Application.Configurations.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The drawer shows an entry when the user holds its permission on any scope, not only the one the circuit works
/// in: a regional user browsing at the root still sees the entries their region's grants open, over scoped and
/// global data alike.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class NavVisibilityTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task RegionalUser_SeesEntriesGrantedBelowTheRoot()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        TestCaller regional = await scenario.CreateGrantedCallerAsync(
            scenario.North, RbacScenario.Read, ScopePermissions.Read.Permission, UserPermissions.Read.Permission);

        string html = await GetHomeAsync(regional);

        Assert.Contains("href=\"/dm/devices\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ua/scopes\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ua/users\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/dm/manufacturers\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserWithNoGrants_SeesNoGatedEntries()
    {
        string html = await GetHomeAsync(new TestCaller());

        Assert.DoesNotContain("href=\"/dm/devices\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/ua/scopes\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedGroup_ShowsOnlyTheEntriesTheUserMaySee()
    {
        string html = await GetHomeAsync(new TestCaller(), withSharedGroupModules: true);

        Assert.Contains("href=\"/open/page\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/gated/page\"", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, ">Administration<"));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The home page, rendered for <paramref name="caller"/>: it has no breadcrumbs, so every link is the drawer's.</summary>
    private async Task<string> GetHomeAsync(IUserContext caller, bool withSharedGroupModules = false)
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(caller));

                if (withSharedGroupModules)
                {
                    SerginUiModuleCatalog real = factory.Services.GetRequiredService<SerginUiModuleCatalog>();
                    services.AddSingleton(new SerginUiModuleCatalog(
                    [
                        .. real.Modules,
                        // Each fake module needs a distinct UiAssembly, or endpoint mapping throws "Assembly already defined";
                        // these SharedKernel assemblies are borrowed because they hold no [Route] components.
                        new SharedGroupModule("open", typeof(SerginNavItem).Assembly, new SerginNavItem(TestText.Of("Open entry"), "/open/page", string.Empty)),
                        new SharedGroupModule(
                            "gated",
                            typeof(IUserContext).Assembly,
                            new SerginNavItem(TestText.Of("Gated entry"), "/gated/page", string.Empty, RequiredPermission: ScopePermissions.Read)),
                    ]));
                }
            }));

        using HttpClient client = host.CreateClient();

        return await client.GetStringAsync(new Uri("/", UriKind.Relative));
    }

    private sealed class FixedUserContextFactory(IUserContext caller) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => caller;
    }

    private sealed class SharedGroupModule(string schema, Assembly uiAssembly, SerginNavItem item) : ISerginWebUiModule
    {
        private static Assembly NoComponents => typeof(SerginNavItem).Assembly;

        public string Name => schema;

        public string Schema => schema;

        public Assembly ApplicationAssembly => NoComponents;

        public Assembly ContractsAssembly => NoComponents;

        public Assembly ConfigurationsAssembly => NoComponents;

        public Assembly UiAssembly => uiAssembly;

        public SerginNavGroup NavGroup => SerginNavGroups.Administration;

        public IReadOnlyCollection<SerginNavItem> NavItems => [item];

        public void AddServices(IServiceCollection services, IConfigurationSection configuration)
        {
        }

        public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;
    }
}
