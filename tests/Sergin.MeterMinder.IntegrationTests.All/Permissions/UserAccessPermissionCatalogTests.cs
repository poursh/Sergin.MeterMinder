using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.UserAccess.Application.Contracts.Roles;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.Create;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.GrantPermission;
using Sergin.UserAccess.Application.Contracts.Roles.Commands.RevokePermission;
using Sergin.UserAccess.Application.Contracts.Scopes;
using Sergin.UserAccess.Application.Contracts.Users;
using Sergin.UserAccess.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Sergin.UserAccess.Infrastructure.Data;

namespace Sergin.MeterMinder.IntegrationTests.All.Permissions;

/// <summary>UserAccess registers its seven permissions per aggregate, and the role commands check them against the catalogue.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class UserAccessPermissionCatalogTests(SerginWebApiFactory<Program> factory)
{
    private const string Unknown = "permission.ua.nothing.here";

    [Fact]
    public void Catalog_HoldsEveryUserAccessPermission()
    {
        PermissionCatalog catalog = factory.Services.GetRequiredService<PermissionCatalog>();

        PermissionCatalogModule module = Assert.Single(catalog.Grouped(), m => m.Schema == "ua");

        Assert.Equal(["Role", "Scope", "User"], module.Groups.Select(g => g.Name));

        PermissionDefinition[] all =
        [
            UserPermissions.Read, UserPermissions.Manage, UserPermissions.AssignmentsManage,
            ScopePermissions.Read, ScopePermissions.Manage,
            RolePermissions.Read, RolePermissions.Manage,
        ];

        Assert.Equal(7, module.Groups.Sum(g => g.Permissions.Count));
        Assert.All(all, definition => Assert.True(catalog.Contains(definition), definition.Permission.Value));
        Assert.Contains(ScopePermissions.Manage, Assert.Single(module.Groups, g => g.Name == "Scope").Permissions);
    }

    [Fact]
    public async Task Grant_OfAPermissionMissingFromTheCatalogue_IsAValidationError()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "growing", inheritable: false, UserPermissions.Read.Permission);

        ErrorOr<GrantRolePermissionCommandResponse> result =
            await tree.SendAtRoleVersionAsync(role, new GrantRolePermissionCommand(role, Permission.Create(Unknown)));

        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(nameof(GrantRolePermissionCommand.Permission), result.FirstError.Code);
    }

    [Fact]
    public async Task Create_WithAPermissionMissingFromTheCatalogue_IsAValidationError()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);

        ErrorOr<CreateRoleCommandResponse> result = await tree.TrySendAsync(
            new CreateRoleCommand(tree.North, new RoleName("made-up"), false, [Permission.Create(Unknown)]));

        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(nameof(CreateRoleCommand.Permissions), result.FirstError.Code);
    }

    [Fact]
    public async Task Grant_OfAllPlatform_Succeeds()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await tree.CreateRoleAsync(tree.North, "everything", inheritable: false, UserPermissions.Read.Permission);

        ErrorOr<GrantRolePermissionCommandResponse> result =
            await tree.SendAtRoleVersionAsync(role, new GrantRolePermissionCommand(role, Permission.AllPlatform));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }

    [Fact]
    public async Task Revoke_OfAPermissionMissingFromTheCatalogue_Succeeds()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        Guid role = await SeedRoleWithUnknownPermissionAsync(tree);

        ErrorOr<RevokeRolePermissionCommandResponse> result =
            await tree.SendAtRoleVersionAsync(role, new RevokeRolePermissionCommand(role, Permission.Create(Unknown)));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
    }

    /// <summary>A role holding a name the catalogue does not know, written around the validator the way an older release could have.</summary>
    internal async Task<Guid> SeedRoleWithUnknownPermissionAsync(RbacScenario tree)
    {
        Guid id = await tree.CreateRoleAsync(tree.North, $"orphan-{Guid.NewGuid():N}"[..20], inheritable: false, UserPermissions.Read.Permission);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DbContext db = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IUserAccessDbContext>());
        Role role = await db.Set<Role>().FindAsync([new RoleId(id)]) ?? throw new InvalidOperationException("role missing");
        role.Grant(Permission.Create(Unknown));
        await db.SaveChangesAsync();

        return id;
    }

    /// <summary>A role holding a stored string that is not even a well-formed permission name, inserted as raw SQL.</summary>
    internal async Task<Guid> SeedRoleWithMalformedPermissionAsync(RbacScenario tree, string malformed)
    {
        Guid id = await tree.CreateRoleAsync(tree.North, $"broken-{Guid.NewGuid():N}"[..20], inheritable: false, UserPermissions.Read.Permission);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        DbContext db = Assert.IsAssignableFrom<DbContext>(scope.ServiceProvider.GetRequiredService<IUserAccessDbContext>());
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ua.role_permissions (role_id, permission, created_at_utc, created_by) VALUES ({0}, {1}, now(), {2})", id, malformed, Guid.Empty);

        return id;
    }
}
