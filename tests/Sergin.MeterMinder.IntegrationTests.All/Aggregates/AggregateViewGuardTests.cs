using Sergin.MeterMinder.IntegrationTests.All.Aggregates.BadOverviewTargets;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.BrokenChildViews;
using Sergin.MeterMinder.IntegrationTests.All.Aggregates.ParentedLists;
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
    public void Guard_GetOneWhoseConstructorIsNotIdOnly_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(BadQueryProbeViewConfiguration)),
            nameof(BadQueryProbeViewConfiguration), nameof(GetBadQueryProbeQuery), "id");

    [Fact]
    public void Guard_EntityOverviewWhoseConstructorIsNotIdOnly_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(BadOverviewProbeViewConfiguration)),
            nameof(BadOverviewProbeViewConfiguration), nameof(GetBadOverviewProbeOverviewQuery), "id");

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

    [Fact]
    public void Guard_SchedulableCommandNotBuiltFromTheId_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(EditScheduledProbeViewConfiguration)),
            nameof(EditScheduledProbeViewConfiguration), nameof(UpdateEditScheduledProbeCommand), "Schedulable");

    [Fact]
    public void Guard_CreateRouteWithoutCreateCommand_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(CreateRouteWithoutCommandViewConfiguration)),
            nameof(CreateRouteWithoutCommandViewConfiguration), $"ICreateCommand<{nameof(PlainProbe)}");

    [Fact]
    public void Guard_EditRouteWithoutUpdateCommand_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(EditRouteWithoutUpdateViewProbeViewConfiguration)),
            nameof(EditRouteWithoutUpdateViewProbeViewConfiguration), $"IUpdateCommand<{nameof(ViewProbe)}");

    [Fact]
    public void Guard_FormCommandWithoutPublicConstructor_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(CtorProbeViewConfiguration)),
            nameof(CtorProbeViewConfiguration), nameof(CreateCtorProbeCommand), "public constructor");

    [Fact]
    public void Guard_ChildWithoutName_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildWithoutNameViewConfiguration)),
            nameof(ChildWithoutNameViewConfiguration), nameof(ViewProbeChild), "Named");

    [Fact]
    public void Guard_ChildOfNoRequest_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(NotAChildViewConfiguration)),
            nameof(NotAChildViewConfiguration), nameof(ViewProbeChild), nameof(ViewProbe));

    [Fact]
    public void Guard_ChildCommandOfAnotherRoot_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildCommandOfAnotherRootViewConfiguration)),
            nameof(ChildCommandOfAnotherRootViewConfiguration), nameof(AddViewProbeChildCommand), typeof(ChildHost).FullName!);

    [Fact]
    public void Guard_ChildFieldOverrideOnUnknownProperty_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildUnknownFieldViewConfiguration)),
            nameof(ChildUnknownFieldViewConfiguration), nameof(ViewProbeChildResponse), "Length");

    [Fact]
    public void Guard_ChildSetPropertyWithoutPageMode_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildSetPropertyWithoutPageModeViewConfiguration)),
            nameof(ChildSetPropertyWithoutPageModeViewConfiguration), nameof(RenameViewProbeChildCommand), "Edit(SerginEditMode.Page)");

    [Fact]
    public void Guard_ChildPageModeWithoutEditRoute_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildPageModeWithoutEditRouteViewConfiguration)),
            nameof(ChildPageModeWithoutEditRouteViewConfiguration), nameof(RenameViewProbeChildCommand), "edit route");

    [Fact]
    public void Guard_ChildTwoPageModeCommands_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildTwoPageModeCommandsViewConfiguration)),
            nameof(ChildTwoPageModeCommandsViewConfiguration), nameof(RenameViewProbeChildCommand), nameof(RemoveViewProbeChildCommand));

    [Fact]
    public void Guard_ChildCreateRouteWithoutAddCommand_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildCreateRouteWithoutAddCommandViewConfiguration)),
            nameof(ChildCreateRouteWithoutAddCommandViewConfiguration), $"IAddChildCommand<{nameof(BareChildHost)}");

    [Fact]
    public void Guard_ChildDetailRouteWithoutQuery_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildDetailRouteWithoutQueryViewConfiguration)),
            nameof(ChildDetailRouteWithoutQueryViewConfiguration), $"IChildGetOneQuery<{nameof(BareChildHost)}");

    [Fact]
    public void Guard_DisabledWhenOnForeignRecord_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildDisabledWhenOnForeignRecordViewConfiguration)),
            nameof(ChildDisabledWhenOnForeignRecordViewConfiguration), nameof(RemoveViewProbeChildCommand), nameof(ViewProbeResponse));

    [Fact]
    public void Guard_RootSetPropertyPage_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(RootSetPropertyViewConfiguration)),
            nameof(RootSetPropertyViewConfiguration), nameof(RenameSetProbeCommand), nameof(SerginEditMode.Page));

    [Fact]
    public void Guard_RootSetPropertyNotIdAndOneValue_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(PairSetPropertyViewConfiguration)),
            nameof(PairSetPropertyViewConfiguration), nameof(SetPairSetProbeCommand), "(Id, value)");

    [Fact]
    public void Guard_InlineWithoutResponseProperty_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(InlineWithoutResponsePropertyViewConfiguration)),
            nameof(InlineWithoutResponsePropertyViewConfiguration), nameof(RenameSetProbeCommand), nameof(SetProbeResponse), "Name");

    [Fact]
    public void Guard_ChildRouteMissingAnId_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildRouteWithWrongIdViewConfiguration)),
            nameof(ChildRouteWithWrongIdViewConfiguration), nameof(ViewProbeChild), "detail", "both");

    [Fact]
    public void Guard_ChildListFilter_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ChildListFilterViewConfiguration)),
            nameof(ChildListFilterViewConfiguration), nameof(ViewProbeChild), "Filter(\"name\"");

    [Fact]
    public void Guard_ChildRouteNoComponentDeclares_Refuses()
    {
        AggregateViewRegistry registry = AggregateViewTestRegistry.Build(typeof(ChildHostViewConfiguration));

        AssertRefused(
            () => AggregateViewGuard.Ensure(registry, [typeof(AggregateViewGuardTests).Assembly]),
            nameof(ChildHostViewConfiguration), nameof(ViewProbeChild), "/tst/child-hosts/");
    }

    [Fact]
    public void Guard_DetailEnumFieldWithoutValueText_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(EnumGapProbeViewConfiguration)),
            nameof(EnumGapProbeViewConfiguration), $"{nameof(EnumGapProbeResponse)}.{nameof(EnumGapProbeResponse.Status)}", $"{nameof(ProbeEnum)}.{nameof(ProbeEnum.Second)}");

    [Fact]
    public void Guard_ReversePairWithoutAppliesWhen_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(PairWithoutAppliesWhenViewConfiguration)),
            nameof(PairWithoutAppliesWhenViewConfiguration), nameof(PublishProbeCommand), nameof(UnpublishProbeCommand), "neither");

    [Fact]
    public void Guard_ReversePairAppliesWhenOnBoth_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(PairAppliesWhenOnBothViewConfiguration)),
            nameof(PairAppliesWhenOnBothViewConfiguration), nameof(PublishProbeCommand), nameof(UnpublishProbeCommand), "both");

    [Fact]
    public void Guard_AppliesWhenOnUnknownRecord_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(AppliesWhenUnknownRecordViewConfiguration)),
            nameof(AppliesWhenUnknownRecordViewConfiguration), nameof(PublishProbeCommand), nameof(ViewProbeResponse), "AppliesWhen");

    [Fact]
    public void Guard_LinkToUnknownRoute_Refuses()
    {
        AggregateViewRegistry registry = AggregateViewTestRegistry.Build(typeof(LinkToUnknownRouteViewConfiguration));

        AssertRefused(
            () => AggregateViewGuard.Ensure(registry, [typeof(AggregateViewGuardTests).Assembly]),
            nameof(LinkToUnknownRouteViewConfiguration), "link", "/nowhere");
    }

    [Fact]
    public void Guard_ParentedListWithoutListUnder_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration), typeof(RoutedViewProbeNoteViewConfiguration)),
            nameof(RoutedViewProbeNoteViewConfiguration), nameof(ListViewProbeNotesQuery), "ListUnder<ViewProbe>");

    [Fact]
    public void Guard_ListUnderWrongParent_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration), typeof(WrongParentViewProbeNoteViewConfiguration)),
            nameof(WrongParentViewProbeNoteViewConfiguration), $"ListUnder<{nameof(ViewOwner)}>", nameof(ViewProbe));

    [Fact]
    public void Guard_ListUnderWithoutParentQuery_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ListUnderViewProbeViewConfiguration)),
            nameof(ListUnderViewProbeViewConfiguration), $"ListUnder<{nameof(ViewOwner)}>", nameof(ListViewProbesQuery));

    [Fact]
    public void Guard_ListUnderParentWithoutView_Refuses() =>
        AssertRefused(
            () => AggregateViewTestRegistry.Build(typeof(ViewProbeNoteViewConfiguration)),
            nameof(ViewProbeNoteViewConfiguration), typeof(ViewProbe).FullName!, "view configuration");

    private static InvalidOperationException AssertRefused(Action build, params string[] named)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(build);

        Assert.All(named, name => Assert.Contains(name, refused.Message, StringComparison.Ordinal));
        return refused;
    }
}
