using ErrorOr;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.UserAccess.Application.Contracts;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Move;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.Assign;
using Sergin.UserAccess.Application.Contracts.Users.Assignments.Commands.Revoke;
using Sergin.UserAccess.Domain.Roles;

namespace Sergin.MeterMinder.IntegrationTests.All.Authorization;

/// <summary>
/// The requirement document's acceptance criteria AC-1 to AC-10, on the tree Company &gt; North Region &gt;
/// Feeder 1 and Feeder 2 (plus South Region). "Ali passes a check" means the scoped permission checker grants
/// him the Operator role's permission on that scope.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class ScopedRbacAcceptanceTests(SerginWebApiFactory<Program> factory)
{
    [Fact]
    public async Task AC1_SubtreeAssignment_GrantsOnTheScopeAndEveryScopeBelow()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");

        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);

        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.North));
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder2));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Company));
    }

    [Fact]
    public async Task AC2_ScopeOnlyAssignment_GrantsOnThatScopeOnly()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");

        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: false)).IsError);

        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.North));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder2));
    }

    [Fact]
    public async Task AC3_RolesDoNotLeakBetweenRegions()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid viewerRole = await tree.CreateRoleAsync(tree.South, "region-viewer", inheritable: true, RbacScenario.Read);
        Guid ali = await tree.CreateUserAsync("ali");

        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);
        Assert.False((await tree.AssignAsync(ali, viewerRole, tree.South, includeSubtree: true)).IsError);

        Assert.True(await tree.HasAsync(ali, RbacScenario.Read, tree.South));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.South));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Read, tree.North));
    }

    [Fact]
    public async Task AC4_AnAdminCannotPutAPermissionTheyLackOnARole()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);

        // Holds role management everywhere, but not the permission it tries to hand out.
        TestCaller admin = new(UserAccessPermissions.RolesManage, RbacScenario.Read);

        ErrorOr<CreateRoleCommandResponse> result = await tree.TrySendAsync(
            new CreateRoleCommand(tree.North, new RoleName("disconnector"), false, [RbacScenario.Update]), admin);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Permissions", result.FirstError.Code);
    }

    [Fact]
    public async Task AC5_ANonInheritableRoleCannotBeAssignedBelowItsOwner()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid localRole = await tree.CreateRoleAsync(tree.North, "north-only", inheritable: false, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");

        ErrorOr<AssignRoleCommandResponse> result = await tree.AssignAsync(ali, localRole, tree.Feeder1, includeSubtree: false);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("ScopeId", result.FirstError.Code);
    }

    [Fact]
    public async Task AC6_AnAssignmentWhosePeriodHasEnded_GrantsNothing()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Guid bea = await tree.CreateUserAsync("bea");

        DateTime now = DateTime.UtcNow;
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, true, now.AddDays(-10), now.AddDays(-1))).IsError);
        Assert.False((await tree.AssignAsync(bea, operatorRole, tree.North, true, now.AddDays(1), null)).IsError);

        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.North));
        Assert.False(await tree.HasAsync(bea, RbacScenario.Update, tree.North));
    }

    [Fact]
    public async Task AC7_ADeactivatedUser_IsDeniedEverywhere()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).IsError);
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));

        await tree.SendAsync(new DeactivateUserCommand(ali));

        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.North));
        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
    }

    [Fact]
    public async Task AC8_AMoveThatStrandsAnAssignmentOutsideItsOwnerSubtree_IsRejected()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Assert.False((await tree.AssignAsync(ali, operatorRole, tree.Feeder1, includeSubtree: false)).IsError);

        ErrorOr<MoveScopeCommandResponse> stranded =
            await tree.TrySendAsync(new MoveScopeCommand(tree.Feeder1, tree.South));

        Assert.True(stranded.IsError);
        Assert.Equal(ErrorType.Validation, stranded.FirstError.Type);

        // Feeder 2 holds no such assignment, so the same move is allowed, and its path follows it.
        ErrorOr<MoveScopeCommandResponse> moved =
            await tree.TrySendAsync(new MoveScopeCommand(tree.Feeder2, tree.South));

        Assert.False(moved.IsError, moved.IsError ? moved.FirstError.Description : string.Empty);

        Guid southRole = await tree.CreateRoleAsync(tree.South, "south-operator", inheritable: true, RbacScenario.Read);
        Assert.False((await tree.AssignAsync(ali, southRole, tree.South, includeSubtree: true)).IsError);
        Assert.True(await tree.HasAsync(ali, RbacScenario.Read, tree.Feeder2));
    }

    [Fact]
    public async Task AC9_ARevokedAssignment_IsDeniedOnTheNextCheck()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid operatorRole = await tree.CreateRoleAsync(tree.North, "operator", inheritable: true, RbacScenario.Update);
        Guid ali = await tree.CreateUserAsync("ali");
        Guid assignment = (await tree.AssignAsync(ali, operatorRole, tree.North, includeSubtree: true)).Value.Id;

        // Warms the cache, so the denial below proves invalidation rather than a cold read.
        Assert.True(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));

        await tree.SendAsync(new RevokeAssignmentCommand(ali, assignment));

        Assert.False(await tree.HasAsync(ali, RbacScenario.Update, tree.Feeder1));
    }

    [Fact]
    public async Task AC10_ACheckThatFails_DeniesAccess()
    {
        await using WebApplicationFactory<Program> failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<IScopedPermissionChecker, ThrowingChecker>()));

        using IServiceScope scope = failing.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestCaller();

        IScopeAuthorization authorization = scope.ServiceProvider.GetRequiredService<IScopeAuthorization>();

        Assert.False(await authorization.CurrentUserHasAsync(RbacScenario.Update, Guid.NewGuid()));
    }

    [Fact]
    public async Task GlobalPermissions_PassEveryScopeWithoutALookup()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestCaller(RbacScenario.Update);

        IScopeAuthorization authorization = scope.ServiceProvider.GetRequiredService<IScopeAuthorization>();

        Assert.True(await authorization.CurrentUserHasAsync(RbacScenario.Update, Guid.NewGuid()));
        Assert.False(await authorization.CurrentUserHasAsync(RbacScenario.Read, Guid.NewGuid()));
    }

    private sealed class ThrowingChecker : IScopedPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, Permission permission, Guid scopeId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The check query failed.");
    }
}
