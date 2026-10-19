using ErrorOr;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.UserAccess.Application.Authorization;
using Sergin.UserAccess.Domain.Roles;
using Sergin.UserAccess.Domain.Scopes;
using Sergin.UserAccess.Domain.Users;
using Sergin.UserAccess.Domain.Users.Assignments;

namespace Sergin.UserAccess.UnitTests.Users;

/// <summary>The assignment business rules, one test each (NFR-7), on Company &gt; North &gt; Feeder 1, plus South.</summary>
public sealed class AssignmentRuleTests
{
    private static readonly Permission Operate = "permission.dm.devices.update";
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly Scope company;
    private readonly Scope north;
    private readonly Scope south;
    private readonly Scope feeder1;
    private readonly User ali = User.Create(new UserName("ali"));

    public AssignmentRuleTests()
    {
        var root = Scope.CreateRoot(new ScopeName("Root"));
        company = Scope.Create(new ScopeName("Company"), root);
        north = Scope.Create(new ScopeName("North"), company);
        south = Scope.Create(new ScopeName("South"), company);
        feeder1 = Scope.Create(new ScopeName("Feeder 1"), north);
    }

    [Fact]
    public void BR2_AnInheritableRole_IsAssignableAtItsOwnerAndBelow_ButNotBesideOrAbove()
    {
        var role = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);

        Assert.True(role.IsAssignableAt(north.Path, north.Path));
        Assert.True(role.IsAssignableAt(north.Path, feeder1.Path));
        Assert.False(role.IsAssignableAt(north.Path, south.Path));
        Assert.False(role.IsAssignableAt(north.Path, company.Path));
    }

    [Fact]
    public void BR2_ANonInheritableRole_IsAssignableAtItsOwnerOnly()
    {
        var role = Role.Create(north.Id, new RoleName("local"), isInheritable: false, [Operate]);

        Assert.False(ali.AssignRole(role, north.Path, north, false, ValidityPeriod.Always, Now).IsError);

        ErrorOr<RoleAssignment> below = ali.AssignRole(role, north.Path, feeder1, false, ValidityPeriod.Always, Now);

        Assert.Equal(ErrorType.Validation, below.FirstError.Type);
        Assert.Equal("ScopeId", below.FirstError.Code);
    }

    [Fact]
    public void AnActiveAssignment_CannotBeDuplicated()
    {
        var role = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);

        Assert.False(ali.AssignRole(role, north.Path, north, true, ValidityPeriod.Always, Now).IsError);
        Assert.True(ali.AssignRole(role, north.Path, north, false, ValidityPeriod.Always, Now).IsError);
    }

    [Fact]
    public void FR8_TheValidityWindow_StartsInclusiveAndEndsExclusive()
    {
        var role = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);
        ValidityPeriod period = ValidityPeriod.Create(Now, Now.AddDays(1)).Value;

        RoleAssignment assignment = ali.AssignRole(role, north.Path, north, true, period, Now).Value;

        Assert.False(assignment.IsActiveAt(Now.AddTicks(-1)));
        Assert.True(assignment.IsActiveAt(Now));
        Assert.False(assignment.IsActiveAt(Now.AddDays(1)));
        Assert.True(ValidityPeriod.Create(Now, Now).IsError);
    }

    [Fact]
    public void FR12_RevokingKeepsTheRow_AndRevokingTwiceIsNotFound()
    {
        var role = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);
        RoleAssignment assignment = ali.AssignRole(role, north.Path, north, true, ValidityPeriod.Always, Now).Value;
        var admin = Guid.NewGuid();

        Assert.False(ali.RevokeAssignment(assignment.Id, Now, admin).IsError);

        Assert.Single(ali.Assignments);
        Assert.False(assignment.IsActiveAt(Now));
        Assert.Equal(admin, assignment.RevokedBy);
        Assert.Equal(ErrorType.NotFound, ali.RevokeAssignment(assignment.Id, Now, admin).FirstError.Type);
    }

    [Fact]
    public void FR13_DeactivatingKeepsTheAssignments()
    {
        var role = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);
        ali.AssignRole(role, north.Path, north, true, ValidityPeriod.Always, Now);

        ali.Deactivate();

        Assert.False(ali.IsActive);
        Assert.Single(ali.Assignments);
    }

    [Fact]
    public void FR7_AGrant_CoversItsScope_AndItsSubtreeOnlyWhenAskedTo()
    {
        EffectiveGrant subtree = new(Guid.NewGuid(), north.Id.Value, north.Path, true, null, null, new HashSet<string> { Operate.Value });
        EffectiveGrant scopeOnly = subtree with { IncludeSubtree = false };

        Assert.True(subtree.Covers(feeder1.Path));
        Assert.False(scopeOnly.Covers(feeder1.Path));
        Assert.True(scopeOnly.Covers(north.Path));
        Assert.False(subtree.Covers(south.Path));
    }

    [Fact]
    public void BR7_ARootOwnedRole_IsTheOnlyKindAssignableGlobally()
    {
        var global = Role.Create(Scope.RootId, new RoleName("viewer"), isInheritable: true, [Operate]);
        var regional = Role.Create(north.Id, new RoleName("operator"), isInheritable: true, [Operate]);

        ali.AssignGlobalRole(global);

        RoleAssignment assignment = Assert.Single(ali.Assignments);
        Assert.Equal(Scope.RootId, assignment.ScopeId);
        Assert.True(assignment.IncludeSubtree);
        Assert.Throws<ArgumentException>(() => ali.AssignGlobalRole(regional));
    }
}
