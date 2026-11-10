using ErrorOr;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.Delete;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.GetAssignable;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.GrantPermission;
using Sergin.Administration.UserAccess.Application.Contracts.Roles.Commands.SetInheritable;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Delete;
using Sergin.Administration.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.Administration.UserAccess.Application.Contracts.Users.Assignments.Commands.GetEffective;
using Sergin.Administration.UserAccess.Domain.Roles;
using Sergin.Administration.UserAccess.Application.Configurations.Roles;
using Sergin.Administration.UserAccess.Application.Configurations.Users;
using Sergin.Administration.UserAccess.Domain.Scopes;
using Sergin.Administration.UserAccess.Domain.Users;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>The business rules beyond the acceptance criteria, end to end through the dispatcher.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopedRbacRuleTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task AssignableRoles_AreTheScopesOwnAndThoseInheritedFromAbove()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid inherited = await tree.CreateRoleAsync(tree.Company, "company-wide", inheritable: true, RbacScenario.Read);
        Guid companyOnly = await tree.CreateRoleAsync(tree.Company, "company-only", inheritable: false, RbacScenario.Read);
        Guid own = await tree.CreateRoleAsync(tree.Feeder1, "feeder-local", inheritable: false, RbacScenario.Read);
        Guid sibling = await tree.CreateRoleAsync(tree.Feeder2, "feeder-two", inheritable: true, RbacScenario.Read);

        IReadOnlyCollection<AssignableRoleItem> roles = await tree.SendAsync(new GetAssignableRolesQueryCommand(tree.Feeder1));
        HashSet<Guid> ids = [.. roles.Select(role => role.Id)];

        Assert.Contains(inherited, ids);
        Assert.Contains(own, ids);
        Assert.DoesNotContain(companyOnly, ids);
        Assert.DoesNotContain(sibling, ids);
    }

    [Fact]
    public async Task ARoleCode_CannotRepeatAlongTheAncestorLine()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Read);

        ErrorOr<CreateRoleCommandResponse> below = await tree.TrySendAsync(
            new CreateRoleCommand(tree.Feeder1, new RoleName("operator"), false, [UserPermissions.Read.Permission]));
        ErrorOr<CreateRoleCommandResponse> above = await tree.TrySendAsync(
            new CreateRoleCommand(tree.Company, new RoleName("operator"), false, [UserPermissions.Read.Permission]));
        ErrorOr<CreateRoleCommandResponse> beside = await tree.TrySendAsync(
            new CreateRoleCommand(tree.South, new RoleName("operator"), false, [UserPermissions.Read.Permission]));

        Assert.Equal(ErrorType.Validation, below.FirstError.Type);
        Assert.Equal(ErrorType.Validation, above.FirstError.Type);
        Assert.False(beside.IsError, beside.IsError ? beside.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task GrantingAPermission_WithinTheCeiling_Succeeds_AndAboveIt_IsRefused()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "growing", inheritable: false, RbacScenario.Read);

        Assert.False((await tree.SendAtRoleVersionAsync(role, new GrantRolePermissionCommand(new RoleId(role), UserPermissions.Manage.Permission))).IsError);

        TestCaller limited = await tree.CreateGrantedCallerAsync(tree.Company, RolePermissions.Manage.Permission, RbacScenario.Read);
        ErrorOr<GrantRolePermissionCommandResponse> refused = await tree.SendAtRoleVersionAsync(
            role, new GrantRolePermissionCommand(new RoleId(role), UserPermissions.AssignmentsManage.Permission), limited);

        Assert.Equal(ErrorType.Validation, refused.FirstError.Type);
    }

    [Fact]
    public async Task ARoleInUse_IsDeletedOnlyWithCascade_WhichRevokesItsAssignments()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, role, tree.Feeder1, includeSubtree: false)).IsError);

        ErrorOr<Deleted> refused = await tree.SendAtRoleVersionAsync(role, new DeleteRoleCommand(new RoleId(role), Cascade: false));
        Assert.Equal(ErrorType.Validation, refused.FirstError.Type);
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));

        ErrorOr<Deleted> deleted = await tree.SendAtRoleVersionAsync(role, new DeleteRoleCommand(new RoleId(role), Cascade: true));
        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
    }

    [Fact]
    public async Task AScopeInUse_IsDeletedOnlyWithCascade_AndOnlyAsALeaf()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.Feeder2, "feeder-operator", inheritable: false, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, role, tree.Feeder2, includeSubtree: false)).IsError);

        Assert.Equal(ErrorType.Validation, (await tree.TrySendAsync(new DeleteScopeCommand(new ScopeId(tree.North), Cascade: true))).FirstError.Type);
        Assert.Equal(ErrorType.Validation, (await tree.TrySendAsync(new DeleteScopeCommand(new ScopeId(tree.Feeder2), Cascade: false))).FirstError.Type);

        ErrorOr<Deleted> deleted = await tree.TrySendAsync(new DeleteScopeCommand(new ScopeId(tree.Feeder2), Cascade: true));

        Assert.False(deleted.IsError, deleted.IsError ? deleted.FirstError.Description : string.Empty);
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder2));
    }

    [Fact]
    public async Task MovingAScope_CarriesItsWholeSubtree()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid southRole = await tree.CreateRoleAsync(tree.South, "south-operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, southRole, tree.South, includeSubtree: true)).IsError);
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));

        ErrorOr<MoveScopeCommandResponse> moved = await tree.TrySendAsync(new MoveScopeCommand(new ScopeId(tree.North), new ScopeId(tree.South)));

        Assert.False(moved.IsError, moved.IsError ? moved.FirstError.Description : string.Empty);
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.North));
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder2));

        ErrorOr<MoveScopeCommandResponse> cycle = await tree.TrySendAsync(new MoveScopeCommand(new ScopeId(tree.South), new ScopeId(tree.Feeder1)));
        Assert.Equal(ErrorType.Validation, cycle.FirstError.Type);
    }

    [Fact]
    public async Task ARoleAssignedBelowItsOwner_CannotBeMadeNonInheritable()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, role, tree.Feeder1, includeSubtree: false)).IsError);

        ErrorOr<SetRoleInheritableCommandResponse> result =
            await tree.SendAtRoleVersionAsync(role, new SetRoleInheritableCommand(new RoleId(role), IsInheritable: false));

        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public async Task EffectivePermissions_ListWhatTheUserHoldsPerScope()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid viewerRole = await tree.CreateRoleAsync(tree.South, "south-viewer", inheritable: false, RbacScenario.Read);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);
        Assert.False((await tree.AssignAsync(ali, viewerRole, tree.South, includeSubtree: false)).IsError);

        IReadOnlyCollection<EffectivePermissionItem> effective =
            await tree.SendAsync(new GetUserEffectivePermissionsQueryCommand(new UserInternalId(ali)));

        EffectivePermissionItem north = Assert.Single(effective, item => item.ScopeId == tree.North);
        EffectivePermissionItem south = Assert.Single(effective, item => item.ScopeId == tree.South);
        Assert.True(north.IncludeSubtree);
        Assert.Equal([RbacScenario.Update.Value], north.Permissions);
        Assert.False(south.IncludeSubtree);
        Assert.Equal([RbacScenario.Read.Value], south.Permissions);
    }
}
