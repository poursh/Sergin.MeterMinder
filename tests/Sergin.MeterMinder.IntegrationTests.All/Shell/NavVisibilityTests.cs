using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// The drawer shows an entry when the user holds its permission on any scope, not only the one the circuit works
/// in: a regional user browsing at the root still sees the entries their region's grants open. An AtRoot entry
/// needs the permission on the root.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class NavVisibilityTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task RegionalUser_SeesEntriesGrantedBelowTheRoot_ButNotAtRootOnes()
    {
        RbacScenario scenario = await RbacScenario.BuildAsync(factory);
        TestCaller regional = await scenario.CreateGrantedCallerAsync(
            scenario.North, RbacScenario.Read, "permission.ua.scopes.read", "permission.ua.users.read");

        string html = await GetHomeAsync(regional);

        Assert.Contains("href=\"/dm/devices\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ua/scopes\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/ua/users\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/dm/manufacturers\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserWithNoGrants_SeesNoGatedEntries()
    {
        string html = await GetHomeAsync(new TestCaller());

        Assert.DoesNotContain("href=\"/dm/devices\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/ua/scopes\"", html, StringComparison.Ordinal);
    }

    /// <summary>The home page, rendered for <paramref name="caller"/>: it has no breadcrumbs, so every link is the drawer's.</summary>
    private async Task<string> GetHomeAsync(IUserContext caller)
    {
        await using WebApplicationFactory<Program> host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IUserContextFactory>(new FixedUserContextFactory(caller))));

        using HttpClient client = host.CreateClient();

        return await client.GetStringAsync(new Uri("/", UriKind.Relative));
    }

    private sealed class FixedUserContextFactory(IUserContext caller) : IUserContextFactory
    {
        public IUserContext CreateUserContext() => caller;
    }
}
