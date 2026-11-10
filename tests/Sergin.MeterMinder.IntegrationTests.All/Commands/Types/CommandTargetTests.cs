using Sergin.SharedKernel.Application.Commands.Types;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

public sealed class CommandTargetTests
{
    [Fact]
    public void TargetOf_EntityCommand_IsEntityOnItsRoot() =>
        Assert.Equal(
            new CommandTarget(typeof(TestTypedRoot), CommandTargetKind.Entity, null, null),
            CommandTypes.TargetOf(typeof(TestDeleteCommand)));

    [Fact]
    public void TargetOf_ChildCommand_CarriesTheChild() =>
        Assert.Equal(
            new CommandTarget(typeof(TestTypedRoot), CommandTargetKind.ChildEntity, typeof(TestTypedChild), null),
            CommandTypes.TargetOf(typeof(TestRemoveChildCommand)));

    [Fact]
    public void TargetOf_AddChild_IsRoot() =>
        Assert.Equal(
            new CommandTarget(typeof(TestTypedRoot), CommandTargetKind.Root, null, null),
            CommandTypes.TargetOf(typeof(TestAddChildCommand)));

    [Fact]
    public void TargetOf_CreateUnderParent_CarriesTheParent() =>
        Assert.Equal(
            new CommandTarget(typeof(TestTypedRoot), CommandTargetKind.Collection, null, typeof(TestTypedRoot)),
            CommandTypes.TargetOf(typeof(TestCreateUnderParentCommand)));

    [Fact]
    public void TargetOf_Create_IsCollection() =>
        Assert.Equal(CommandTargetKind.Collection, CommandTypes.TargetOf(typeof(TestCreateCommand))!.Kind);

    [Fact]
    public void TargetOf_UntypedRecord_IsNull() =>
        Assert.Null(CommandTypes.TargetOf(typeof(TestUntypedCommand)));

    [Fact]
    public void TargetOf_TwoRoots_ThrowsNamingTheType()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandTypes.TargetOf(typeof(TestTwoRootsCommand)));
        Assert.Contains(nameof(TestTwoRootsCommand), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TargetOf_TwoKindsOnOneRoot_ThrowsNamingTheType()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CommandTypes.TargetOf(typeof(TestTwoKindsCommand)));
        Assert.Contains(nameof(TestTwoKindsCommand), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EffectsOf_ReturnsOpenGenericDefinitions() =>
        Assert.Equal([typeof(IDeleteCommand<,>)], CommandTypes.EffectsOf(typeof(TestDeleteCommand)));

    [Fact]
    public void EffectsOf_UpdateArities_AreDistinct()
    {
        Assert.Equal([typeof(IUpdateCommand<,>)], CommandTypes.EffectsOf(typeof(TestUpdateCommand)));
        Assert.Equal([typeof(IUpdateCommand<,,,>)], CommandTypes.EffectsOf(typeof(TestUpdateChildCommand)));
    }

    [Fact]
    public void EffectsOf_ReverseTransition_IgnoresIReverseOf() =>
        Assert.Equal([typeof(ITransitionCommand<,>)], CommandTypes.EffectsOf(typeof(TestReverseTransitionCommand)));

    [Fact]
    public void EffectsOf_SystemCommand_IsSystem()
    {
        Assert.Equal([typeof(ISystemCommand)], CommandTypes.EffectsOf(typeof(TestSystemCommand)));
        Assert.True(CommandTypes.IsSystem(typeof(TestSystemCommand)));
        Assert.False(CommandTypes.IsSystem(typeof(TestDeleteCommand)));
    }

    [Fact]
    public void IsSelectable_ReadsTheMarker()
    {
        Assert.True(CommandTypes.IsSelectable(typeof(TestSelectableDeleteCommand)));
        Assert.False(CommandTypes.IsSelectable(typeof(TestDeleteCommand)));
    }

    [Fact]
    public void EffectsOf_UntypedRecord_IsEmpty() =>
        Assert.Empty(CommandTypes.EffectsOf(typeof(TestUntypedCommand)));
}
