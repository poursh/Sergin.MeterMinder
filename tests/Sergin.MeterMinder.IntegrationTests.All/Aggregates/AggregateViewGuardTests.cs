using Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnlistedTargets;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.UnresolvedFilters;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// Each view configuration mistake refuses the registry (or, for routes, the start guard), naming the configuration and
/// the offending type, property or route.
/// </summary>
public sealed class AggregateViewGuardTests
{
    [Fact]
    public void Guard_TwoConfigurationsForOneRoot_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration), typeof(SecondViewProbeViewConfiguration)),
            nameof(ViewProbeViewConfiguration), nameof(SecondViewProbeViewConfiguration), typeof(ViewProbe).FullName!);

    [Fact]
    public void Guard_ConfigurationWithoutParameterlessConstructor_Refuses() =>
        AssertRefused(() => AggregateViewTestRegistry.Build(typeof(ArgumentViewProbeViewConfiguration)), nameof(ArgumentViewProbeViewConfiguration));

    [Fact]
    public void Guard_CommandOfAnotherRoot_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ForeignCommandViewProbeViewConfiguration)),
            nameof(ForeignCommandViewProbeViewConfiguration), nameof(RenameViewOwnerCommand), nameof(ViewOwner));

    [Fact]
    public void Guard_TransitionWithoutLabel_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnlabelledViewProbeViewConfiguration)),
            nameof(UnlabelledViewProbeViewConfiguration), nameof(RunViewProbeCommand));

    [Fact]
    public void Guard_TransitionWhoseConstructorIsNotIdOnly_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(RunWithNoteViewProbeViewConfiguration)),
            nameof(RunWithNoteViewProbeViewConfiguration), nameof(RunWithNoteNoteProbeCommand), "id");

    [Fact]
    public void Guard_DetailOfAnotherResponse_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(WrongDetailViewProbeViewConfiguration)),
            nameof(WrongDetailViewProbeViewConfiguration), nameof(ViewOwnerResponse), nameof(ViewProbeResponse));

    [Fact]
    public void Guard_ListOfAnotherItem_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(WrongListViewProbeViewConfiguration)),
            nameof(WrongListViewProbeViewConfiguration), nameof(ViewProbeResponse), nameof(ViewProbeListItem));

    [Fact]
    public void Guard_FieldOverrideOnUnknownProperty_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnknownFieldViewProbeViewConfiguration)),
            nameof(UnknownFieldViewProbeViewConfiguration), nameof(ViewProbeResponse), "Length");

    [Fact]
    public void Guard_RouteNoComponentDeclares_Refuses()
    {
        AggregateViewRegistry registry = AggregateViewTestRegistry.Build(typeof(UnroutedViewProbeViewConfiguration));

        InvalidOperationException refused = AssertRefused(
            () => AggregateViewGuard.Ensure(registry, [typeof(AggregateViewGuardTests).Assembly]),
            nameof(UnroutedViewProbeViewConfiguration), "/tst/nowhere");

        // The detail route matches the probe page's /tst/probes/{Id:guid} template, so only the list route is named.
        Assert.DoesNotContain("/tst/probes/", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_ChildIdFormFieldWithoutChildPicker_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(PartHolderWithoutPickerViewConfiguration)),
            nameof(PartHolderWithoutPickerViewConfiguration), nameof(CreatePartHolderCommand), nameof(CreatePartHolderCommand.PartId));

    [Fact]
    public void Guard_RoutesWithoutQueries_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(QuerylessProbeViewConfiguration)),
            nameof(QuerylessProbeViewConfiguration), $"IGetOneQuery<{nameof(QuerylessProbe)}");

    [Fact]
    public void Guard_FilterOnUnknownListField_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnknownFilterViewProbeViewConfiguration)),
            nameof(UnknownFilterViewProbeViewConfiguration), "gadgetId");

    [Fact]
    public void Guard_IdentifierFilterNamingNoTarget_RefusesWithoutAPicker()
    {
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnresolvedFilterViewConfiguration)),
            nameof(UnresolvedFilterViewConfiguration), "gadgetId");

        ListFilterView picked = AggregateViewTestRegistry.Build(typeof(PickedFilterViewConfiguration)).For<FilterProbe>().ListFilters.Single();
        Assert.Equal(typeof(MudBlazor.MudText), picked.Component);
    }

    [Fact]
    public void Guard_IdentifierFilterOnARootWithNoListQuery_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnlistedFilterViewConfiguration)),
            nameof(UnlistedFilterViewConfiguration), "unlistedThingId", "IListOf");

    [Fact]
    public void Guard_FormPickerOnARootWithNoListQuery_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(UnlistedPickerViewConfiguration)),
            nameof(UnlistedPickerViewConfiguration), nameof(CreateUnlistedProbeCommand), nameof(CreateUnlistedProbeCommand.ThingId),
            $"IListOf<{nameof(UnlistedThing)}>");

    private static InvalidOperationException AssertRefused(Action build, params string[] named)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(build);

        Assert.All(named, name => Assert.Contains(name, refused.Message, StringComparison.Ordinal));
        return refused;
    }
}
