using Sergin.SharedKernel.Presentation.Blazor.Trees;

namespace Sergin.MeterMinder.IntegrationTests.All.Shell;

/// <summary>
/// How a flat, self-referencing list becomes the tree <c>SerginAggregateTree</c> shows. No database.
/// </summary>
public sealed class TreeIndexTests
{
    private static readonly Node[] Nodes =
    [
        new("feeder-2", "north", "Feeder 2"),
        new("north", "company", "North"),
        new("company", null, "Company"),
        new("feeder-1", "north", "Feeder 1"),
        new("south", "company", "South"),
        new("orphan", "unreachable", "Orphan"),
    ];

    [Fact]
    public void ARootIsANodeWithNoParent_OrOneTheListDoesNotHold()
    {
        SerginTreeIndex<Node> index = Build(Nodes);

        Assert.Equal(["company", "orphan"], index.Roots.Select(node => node.Id));
    }

    [Fact]
    public void Children_AreUnderTheirParent_ByLabel()
    {
        SerginTreeIndex<Node> index = Build(Nodes);

        Assert.Equal(["feeder-1", "feeder-2"], index.ChildrenOf("north").Select(node => node.Id));
        Assert.Equal(2, index.ChildCount("company"));
        Assert.Equal(0, index.ChildCount("feeder-1"));
        Assert.Empty(index.ChildrenOf("unknown"));
    }

    [Fact]
    public void PathTo_RunsFromTheRootDownToTheNode()
    {
        SerginTreeIndex<Node> index = Build(Nodes);

        Assert.Equal(["company", "north", "feeder-2"], index.PathTo("feeder-2").Select(node => node.Id));
        Assert.Equal(["orphan"], index.PathTo("orphan").Select(node => node.Id));
        Assert.Empty(index.PathTo("unknown"));
    }

    [Fact]
    public void ALoopIsCut_WhereItRepeats()
    {
        SerginTreeIndex<Node> index = Build([new("a", "b", "A"), new("b", "a", "B"), new("self", "self", "Self")]);

        // Each of a and b has its parent in the list, so neither is a root; self names itself and is one.
        Assert.Equal(["self"], index.Roots.Select(node => node.Id));
        Assert.Equal(["b", "a"], index.PathTo("a").Select(node => node.Id));
    }

    private static SerginTreeIndex<Node> Build(IEnumerable<Node> nodes) =>
        new(nodes, node => node.Id, node => node.ParentId, node => node.Name);

    private sealed record Node(string Id, string? ParentId, string Name);
}
