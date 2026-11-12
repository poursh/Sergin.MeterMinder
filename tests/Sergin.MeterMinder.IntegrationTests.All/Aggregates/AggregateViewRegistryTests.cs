using System.Globalization;
using MudBlazor;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the view registry derives for a configured root: each command's policy from its configuration and type, the
/// role defaults, the fields of the GetOne response, the form inputs and the identifier filters.
/// </summary>
public sealed class AggregateViewRegistryTests
{
    private static AggregateViewModel Probe(Type configuration) => AggregateViewTestRegistry.Build(configuration).For<ViewProbe>();

    private static AggregateViewModel Probe() => Probe(typeof(ViewProbeViewConfiguration));

    [Fact]
    public void Registry_ReadsPermissionScopeVersionAndSchedule_FromCommandConfiguration()
    {
        CommandView run = Probe().Commands.Single(command => command.Request == typeof(RunViewProbeCommand));

        Assert.Equal(CommandRole.Transition, run.Role);
        Assert.Equal([ViewProbePermissions.Run], run.Permissions);
        Assert.True(run.AnyScope);
        Assert.True(run.RequiresVersion);
        Assert.Equal("tst.view-probes.run", run.ScheduleName);
        Assert.True(run.Selectable);
        Assert.Equal(ViewProbeTexts.Run, run.Label);
        Assert.Equal(SerginActionKind.Secondary, run.Kind);
    }

    [Fact]
    public void Registry_DeleteGetsDefaultLabelKindAndIcon()
    {
        CommandView delete = Probe().Commands.Single(command => command.Request == typeof(DeleteViewProbeCommand));

        Assert.Equal(CommandRole.Delete, delete.Role);
        Assert.Equal(CommonTexts.Delete, delete.Label);
        Assert.Equal(SerginActionKind.Danger, delete.Kind);
        Assert.Equal(Icons.Material.Outlined.Delete, delete.Icon);
        Assert.False(delete.Selectable);
    }

    [Fact]
    public void Registry_CreateGetsDefaultLabelKindAndIcon()
    {
        CommandView create = Probe().Commands.Single(command => command.Role == CommandRole.Create);

        Assert.Equal(typeof(CreateViewProbeCommand), create.Request);
        Assert.Equal(CommonTexts.Create, create.Label);
        Assert.Equal(SerginActionKind.Primary, create.Kind);
        Assert.Equal(Icons.Material.Filled.Add, create.Icon);
        Assert.False(create.RequiresVersion);
    }

    [Fact]
    public void Registry_DetailFields_HideGuidsAndUnlabelled_KeepDeclarationOrder()
    {
        AggregateViewModel probe = Probe();

        Assert.Equal(["Name"], probe.DetailFields.Where(field => !field.Hidden).Select(field => field.Name));
        Assert.Equal(ViewProbeTexts.Name, probe.DetailFields.Single().Label);
        Assert.Equal("Name", probe.DetailTitleField);
        Assert.Equal("probe", probe.DetailFields.Single().Get(new ViewProbeResponse(Guid.Empty, new ViewProbeName("probe"), 3)));
    }

    [Fact]
    public void Registry_DetailArguments_ReadValueObjectsThroughToTheirValue()
    {
        AggregateViewModel probe = Probe();
        ViewProbeResponse record = new(Guid.Empty, new ViewProbeName("probe"), 3);

        IReadOnlyDictionary<string, object?> arguments = probe.DetailArguments!(record);

        Assert.Equal("probe", arguments["Name"]);
        Assert.Equal(3, arguments["Hits"]);
        Assert.Equal("Delete probe? 3 hits.", TextPlaceholders.Format("Delete {Name}? {Hits} hits.", arguments, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Registry_FieldOverride_HidesALabelledField() =>
        Assert.All(Probe(typeof(HiddenNameViewProbeViewConfiguration)).DetailFields, field => Assert.True(field.Hidden));

    [Fact]
    public void Registry_HasSchedulableCommand_WhenAnyCommandIsSchedulable()
    {
        Assert.True(Probe().HasSchedulableCommand);
        Assert.False(AggregateViewTestRegistry.Build(typeof(PartHolderWithPickerViewConfiguration)).For<PartHolder>().HasSchedulableCommand);
    }

    [Fact]
    public void Registry_JoinsQueriesTitleSchemaAndFeatures()
    {
        AggregateViewModel probe = Probe();

        Assert.Equal(typeof(GetViewProbeQuery), probe.GetOne);
        Assert.Equal(typeof(ListViewProbesQuery), probe.List);
        Assert.Equal(typeof(CreateViewProbeCommand), probe.Create);
        Assert.Null(probe.Update);
        Assert.Equal(ViewProbeTexts.Title, probe.Title);
        Assert.Equal(ViewProbeTexts.Singular, probe.Singular);
        Assert.Equal("tst", probe.Schema);
        Assert.Equal(typeof(ViewProbeViewConfiguration), probe.Configuration);
        Assert.True(probe.Features.Versioned);
        Assert.Same(ViewProbeListFields.Set, probe.ListFieldSet);
        Assert.Equal("/tst/view-probes", probe.Routes.List);
    }

    [Fact]
    public void Registry_CreateFields_TextFromTheWrappedStringAndPickerFromTheIdType()
    {
        IReadOnlyList<FormFieldView> fields = Probe().CreateFields;

        Assert.Equal(["Name", "OwnerId"], fields.Select(field => field.Name));
        Assert.Equal(FormInputKind.Text, fields[0].Kind);
        Assert.Equal(FormInputKind.Picker, fields[1].Kind);
        Assert.Equal(typeof(ViewOwner), fields[1].PickerRoot);
        Assert.Equal(typeof(ViewOwnerId), fields[1].ValueType);
    }

    [Fact]
    public void Registry_ChildPicker_WhenConfigured()
    {
        FormFieldView part = AggregateViewTestRegistry.Build(typeof(PartHolderWithPickerViewConfiguration))
            .For<PartHolder>().CreateFields.Single();

        Assert.Equal(FormInputKind.ChildPicker, part.Kind);
        Assert.Equal(typeof(ViewPart), part.PickerRoot);
        Assert.Equal(typeof(ViewOwner), part.PickerParentRoot);

        var owner = Guid.CreateVersion7();
        Assert.Equal(owner, part.ParentFromRecord!(new PartHolderResponse(Guid.Empty, owner)));
    }

    [Fact]
    public void Registry_IdentifierFilters_ResolveARootAndAChildUnderItsRootByName()
    {
        IReadOnlyList<ListFilterView> filters = Probe().ListFilters;

        ListFilterView owner = filters.Single(filter => filter.Field.Id == "viewOwnerId");
        Assert.Equal(typeof(ViewOwner), owner.TargetRoot);
        Assert.Null(owner.TargetChild);

        ListFilterView part = filters.Single(filter => filter.Field.Id == "viewPartId");
        Assert.Equal(typeof(ViewOwner), part.TargetRoot);
        Assert.Equal(typeof(ViewPart), part.TargetChild);
        Assert.Equal("viewOwnerId", part.ParentFilterId);
    }

    [Fact]
    public void Registry_HiddenTransition_NeedsNoLabel()
    {
        CommandView run = Probe(typeof(HiddenRunViewProbeViewConfiguration)).Commands.Single(command => command.Request == typeof(RunViewProbeCommand));

        Assert.True(run.Hidden);
    }

    [Fact]
    public void Registry_Find_IsNullForAnUnconfiguredRoot()
    {
        AggregateViewRegistry registry = AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration));

        Assert.Null(registry.Find(typeof(ViewOwner)));
        Assert.Single(registry.All);
    }
}
