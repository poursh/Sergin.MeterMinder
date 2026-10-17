using ErrorOr;
using Sergin.UserAccess.Domain.Scopes;

namespace Sergin.UserAccess.UnitTests.Scopes;

public sealed class ScopeTreeTests
{
    private readonly Scope root = Scope.CreateRoot(new ScopeName("Root"));

    [Fact]
    public void AChildPath_IsItsParentsPathPlusItsOwnLabel()
    {
        var company = Scope.Create(new ScopeName("Company"), root);
        var north = Scope.Create(new ScopeName("North"), company);

        Assert.Equal($"{root.Id.Label}.{company.Id.Label}.{north.Id.Label}", north.Path.Value);
        Assert.Equal(3, north.Path.Depth);
        Assert.Equal(company.Id, north.ParentId);
    }

    [Fact]
    public void IsAncestorOrSelfOf_MatchesWholeLabelsOnly()
    {
        var a = new ScopePath("aa.bb");

        Assert.True(a.IsAncestorOrSelfOf(new ScopePath("aa.bb")));
        Assert.True(a.IsAncestorOrSelfOf(new ScopePath("aa.bb.cc")));
        Assert.False(a.IsAncestorOrSelfOf(new ScopePath("aa.bbx")));
        Assert.False(a.IsAncestorOrSelfOf(new ScopePath("aa")));
    }

    [Fact]
    public void MoveUnder_RepathsTheWholeSubtree()
    {
        var company = Scope.Create(new ScopeName("Company"), root);
        var north = Scope.Create(new ScopeName("North"), company);
        var south = Scope.Create(new ScopeName("South"), company);
        var feeder = Scope.Create(new ScopeName("Feeder"), north);

        ErrorOr<Success> moved = north.MoveUnder(south, [feeder], DateTime.UnixEpoch);

        Assert.False(moved.IsError);
        Assert.Equal(south.Id, north.ParentId);
        Assert.Equal(south.Path.Child(north.Id), north.Path);
        Assert.Equal(north.Path.Child(feeder.Id), feeder.Path);
    }

    [Fact]
    public void MoveUnder_RefusesTheRootAndAnyCycle()
    {
        var company = Scope.Create(new ScopeName("Company"), root);
        var north = Scope.Create(new ScopeName("North"), company);

        Assert.True(root.MoveUnder(company, [], DateTime.UnixEpoch).IsError);
        Assert.True(company.MoveUnder(north, [north], DateTime.UnixEpoch).IsError);
        Assert.True(company.MoveUnder(company, [north], DateTime.UnixEpoch).IsError);
    }
}
