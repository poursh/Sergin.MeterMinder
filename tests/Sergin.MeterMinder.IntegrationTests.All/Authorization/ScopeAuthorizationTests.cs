using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// A handler's own check cannot be scanned at start, so a definition no aggregate registered is refused when it is
/// asked: denied for a signed-in user, whatever they hold. A system identity still passes without a lookup.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopeAuthorizationTests(SerginWebApiFactory<Program> factory)
{
    private static readonly PermissionDefinition Unregistered =
        PermissionDefinition.Create("permission.test.unregistered.read", "Never registered");

    [Fact]
    public async Task CurrentUserHasAsync_OfAnUnregisteredDefinition_Denies()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller admin = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, PermissionDefinition.AllPlatform.Permission);

        using IServiceScope scope = ScopeFor(admin);
        IScopeAuthorization authorization = scope.ServiceProvider.GetRequiredService<IScopeAuthorization>();

        // The control: the same caller passes a registered definition.
        Assert.True(await authorization.CurrentUserHasAsync(DevicePermissions.Read));

        Assert.False(await authorization.CurrentUserHasAsync(Unregistered));
        Assert.False(await authorization.CurrentUserHasAsync(Unregistered, tree.North));
        Assert.False(await authorization.CurrentUserHoldsAnywhereAsync(Unregistered));
        Assert.Same(ScopeReach.Nothing, await authorization.CurrentUserReachAsync(Unregistered));
    }

    [Fact]
    public async Task SystemIdentity_PassesAnUnregisteredDefinition()
    {
        using IServiceScope scope = ScopeFor(new TestSystemCaller());
        IScopeAuthorization authorization = scope.ServiceProvider.GetRequiredService<IScopeAuthorization>();

        Assert.True(await authorization.CurrentUserHasAsync(Unregistered));
        Assert.Same(ScopeReach.Everything, await authorization.CurrentUserReachAsync(Unregistered));
    }

    private IServiceScope ScopeFor(IUserContext caller)
    {
        IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        return scope;
    }
}
