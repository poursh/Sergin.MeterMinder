using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// <see cref="IScopeDirectory"/>, what the shell's scope picker offers: every scope an active grant reaches, whatever
/// the grant's permissions, with a listed scope whose parent is not listed shown as a top-level node.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopeDirectoryTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task DevUser_WithARootGrant_ReachesTheWholeTree()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);

        IReadOnlyList<ScopeOption> options = await ReachableAsync(caller: null);

        Assert.Contains(options, option => option.Id == AuthorizationScope.RootId && option.ParentId is null);
        Assert.All([tree.Company, tree.North, tree.South, tree.Feeder1, tree.Feeder2], id => Assert.Contains(options, option => option.Id == id));
        Assert.Equal(AuthorizationScope.RootId, ScopeOption.HomeOf(options)?.Id);
    }

    [Fact]
    public async Task SubtreeGrant_WithoutScopesRead_ReachesItsScopeAndBelow_AsATopLevelNode()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller caller = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Read);

        IReadOnlyList<ScopeOption> options = await ReachableAsync(caller);

        // Depth first, then name: the region, then its feeders. Company, South and the root stay out.
        Assert.Equal([tree.North, tree.Feeder1, tree.Feeder2], options.Select(option => option.Id));
        Assert.Null(options.Single(option => option.Id == tree.North).ParentId);
        Assert.Equal(tree.North, options.Single(option => option.Id == tree.Feeder1).ParentId);
        Assert.Equal(tree.North, ScopeOption.HomeOf(options)?.Id);
    }

    [Fact]
    public async Task ScopeOnlyGrant_ReachesThatScopeAlone()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid user = await tree.CreateUserAsync("scope-only");
        Assert.False((await tree.AssignAsync(user, role, tree.North, includeSubtree: false)).IsError);

        IReadOnlyList<ScopeOption> options = await ReachableAsync(new TestCaller { Id = new UserId(user) });

        ScopeOption only = Assert.Single(options);
        Assert.Equal(tree.North, only.Id);
        Assert.Null(only.ParentId);
    }

    [Fact]
    public async Task GrantNotYetActive_ReachesNothing()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid user = await tree.CreateUserAsync("future");
        Assert.False((await tree.AssignAsync(user, role, tree.North, includeSubtree: true, fromUtc: DateTime.UtcNow.AddDays(1))).IsError);

        Assert.Empty(await ReachableAsync(new TestCaller { Id = new UserId(user) }));
    }

    [Fact]
    public async Task CallerWithNoGrant_ReachesNothing()
    {
        await RbacScenario.BuildAsync(factory);

        Assert.Empty(await ReachableAsync(new TestCaller()));
    }

    [Fact]
    public async Task SystemIdentity_ReachesEveryScope()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);

        IReadOnlyList<ScopeOption> options = await ReachableAsync(new TestSystemCaller());

        Assert.Contains(options, option => option.Id == AuthorizationScope.RootId);
        Assert.Contains(options, option => option.Id == tree.South);
    }

    [Fact]
    public void HomeOf_IsTheShallowestScope_TiesByName()
    {
        ScopeOption north = new(Guid.NewGuid(), null, "North", 3);
        ScopeOption beta = new(Guid.NewGuid(), null, "beta", 2);
        ScopeOption alpha = new(Guid.NewGuid(), null, "Alpha", 2);

        Assert.Equal(alpha, ScopeOption.HomeOf([north, beta, alpha]));
        Assert.Null(ScopeOption.HomeOf([]));
    }

    private async Task<IReadOnlyList<ScopeOption>> ReachableAsync(IUserContext? caller)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        return await scope.ServiceProvider.GetRequiredService<IScopeDirectory>().GetReachableAsync();
    }
}
