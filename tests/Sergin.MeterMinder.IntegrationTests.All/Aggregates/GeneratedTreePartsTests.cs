using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>What a generated tree page derives from an aggregate's view model.</summary>
public sealed class GeneratedTreePartsTests
{
    private static AggregateViewModel ParentProbeModel() =>
        AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>();

    [Fact]
    public void For_ParentProbe_DerivesRoutesKeysAndTrail()
    {
        var parts = GeneratedTreeParts.For(ParentProbeModel());
        var id = Guid.NewGuid();

        Assert.Equal(ParentProbeRoutes.List, parts.Href);
        Assert.Equal(ParentProbeRoutes.Detail(id), parts.NodeHref(id));
        Assert.Equal("tst." + ParentProbeRoutes.List.Split((char)47)[^1], parts.LayoutKey);
        Assert.Equal("parentprobe", parts.GroupKey);
        Assert.Equal(ParentProbeRoutes.List, Assert.Single(parts.Trail).Href);
        Assert.Equal(typeof(DeleteParentProbeCommand), parts.Delete!.Request);
        Assert.Equal(typeof(CreateParentProbeCommand), parts.AddChild!.Request);
    }

    [Fact]
    public void GeneratedTreeParts_DeleteAndAddChild_CarryTheCommandsPermissions()
    {
        var parts = GeneratedTreeParts.For(ParentProbeModel());

        Assert.Empty(parts.Delete!.Permissions);
        Assert.True(parts.Delete.AnyScope);
        Assert.Equal([ParentProbePermissions.Create], parts.AddChild!.Permissions);
        Assert.True(parts.AddChild.AnyScope);
    }

    [Fact]
    public void For_ARootWithoutATree_Throws_NamingTheRoot()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration)).For<ViewProbe>();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => GeneratedTreeParts.For(model));

        Assert.Contains(typeof(ViewProbe).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_ATreeRootWithoutAListRoute_Throws_NamingTheRoot()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(NoListRouteTreeProbeViewConfiguration)).For<ParentProbe>();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => GeneratedTreeParts.For(model));

        Assert.Contains(typeof(ParentProbe).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteRequest_FillsConfirmValuesWithDefaults()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ConfirmProbeViewConfiguration)).For<ConfirmProbe>();
        CommandView delete = model.Commands.Single(command => command.Role == CommandRole.Delete);
        var id = Guid.NewGuid();

        DeleteConfirmProbeCommand command = Assert.IsType<DeleteConfirmProbeCommand>(GeneratedTreeParts.DeleteRequest(delete, id));

        Assert.Equal(id, command.Id.Value);
        Assert.False(command.Cascade);
    }

    [Fact]
    public void DeleteRequest_IdOnly_UsesBuildFromId()
    {
        CommandView delete = ParentProbeModel().Commands.Single(command => command.Role == CommandRole.Delete);
        var id = Guid.NewGuid();

        DeleteParentProbeCommand command = Assert.IsType<DeleteParentProbeCommand>(GeneratedTreeParts.DeleteRequest(delete, id));

        Assert.Equal(id, command.Id.Value);
    }
}
