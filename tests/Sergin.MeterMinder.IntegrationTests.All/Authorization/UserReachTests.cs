using ErrorOr;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.GetEffective;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.GetList;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Commands.DeactivateUser;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.Administration.UserAccess.Application.Configurations.Users;
using Sergin.Administration.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// UserReachGuard: a user belongs to no scope, so the pipeline passes a regional holder of the user permissions, and
/// the handler then asks that every live assignment of the target lies inside the caller's reach. A regional
/// administrator acts on their region's users and on users with no assignment, never on the root administrator.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class UserReachTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Permission UsersRead = UserPermissions.Read.Permission;
    private static readonly Permission UsersManage = UserPermissions.Manage.Permission;

    [Fact]
    public async Task RegionalAdministrator_CannotActOnSomeoneGrantedAboveTheirRegion()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller rootAdmin = await tree.CreateGrantedCallerAsync(Scope.RootId.Value, UsersRead, UsersManage);
        TestCaller northAdmin = await tree.CreateGrantedCallerAsync(tree.North, UsersRead, UsersManage);
        TestCaller southUser = await tree.CreateGrantedCallerAsync(tree.South, RbacScenario.Read);

        Guid root = rootAdmin.Id.Value;
        Guid south = southUser.Id.Value;

        Assert.Equal(ErrorType.Forbidden, (await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(root)), northAdmin)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await tree.TrySendAsync(new GetUserAssignmentsQueryCommand(new UserInternalId(root)), northAdmin)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await tree.TrySendAsync(new GetUserEffectivePermissionsQueryCommand(new UserInternalId(root)), northAdmin)).FirstError.Type);

        // Beside the region is outside it too.
        Assert.Equal(ErrorType.Forbidden, (await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(south)), northAdmin)).FirstError.Type);

        // The root administrator reaches everyone.
        Assert.False((await tree.TrySendAsync(new GetUserAssignmentsQueryCommand(new UserInternalId(south)), rootAdmin)).IsError);
        Assert.False((await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(south)), rootAdmin)).IsError);
    }

    [Fact]
    public async Task RegionalAdministrator_ActsOnTheirRegionsUsers_AndOnUsersWithNoAssignment()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller northAdmin = await tree.CreateGrantedCallerAsync(tree.North, UsersRead, UsersManage);
        TestCaller feederUser = await tree.CreateGrantedCallerAsync(tree.Feeder1, RbacScenario.Read);
        Guid unassigned = await tree.CreateUserAsync("unassigned");

        Guid feeder = feederUser.Id.Value;

        Assert.NotEmpty((await tree.TrySendAsync(new GetUserAssignmentsQueryCommand(new UserInternalId(feeder)), northAdmin)).Value);
        Assert.NotEmpty((await tree.TrySendAsync(new GetUserEffectivePermissionsQueryCommand(new UserInternalId(feeder)), northAdmin)).Value);
        Assert.False((await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(feeder)), northAdmin)).IsError);
        Assert.False((await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(unassigned)), northAdmin)).IsError);

        // No user permission anywhere is refused by the pipeline, whoever the target is.
        Assert.Equal(ErrorType.Forbidden, (await tree.TrySendAsync(new DeactivateUserCommand(new UserInternalId(unassigned)), new TestCaller())).FirstError.Type);
    }
}
