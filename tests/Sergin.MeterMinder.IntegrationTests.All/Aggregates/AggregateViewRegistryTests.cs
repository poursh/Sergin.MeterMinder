using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;
using Sergin.SharedKernel.IntegrationTests;
using MudBlazor;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// What the view registry derives for a configured root: each command's policy from its configuration and type, the
/// role defaults, the fields of the GetOne response, the form inputs and the identifier filters.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AggregateViewRegistryTests(SerginWebApiFactory<Program> factory)
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
    public void Registry_ProbeEntityOverview_IsIdOnly()
    {
        ViewRequest<Func<Guid, object>> overview = Assert.IsType<ViewRequest<Func<Guid, object>>>(Probe().EntityOverview);
        var id = Guid.CreateVersion7();

        Assert.Equal(typeof(GetViewProbeOverviewQuery), overview.Type);
        Assert.Equal(new GetViewProbeOverviewQuery(new ViewProbeId(id)), overview.Build(id));
        Assert.False(Probe().DetailOverviewCollapsed);
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
        Assert.Equal(ViewProbeTexts.Name.Label, probe.DetailFields.Single().Label);
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
    public void ViewModel_EnumField_HasValueTexts()
    {
        FieldView status = PlainProbe().DetailFields.Single(field => field.Name == nameof(PlainProbeResponse.Status));

        Assert.NotNull(status.ValueTexts);
        Assert.Equal(PlainProbeTexts.StatusValues.For(ProbeEnum.First), status.ValueTexts[ProbeEnum.First]);
        Assert.Equal(PlainProbeTexts.StatusValues.For(ProbeEnum.Second), status.ValueTexts[ProbeEnum.Second]);
        Assert.NotNull(PlainProbe().ListFields.Single(field => field.Name == nameof(PlainProbeListItem.Status)).ValueTexts);
    }

    [Fact]
    public void ViewModel_BoolField_DefaultsToYesNo_OrDeclaredTexts()
    {
        IReadOnlyList<FieldView> fields = PlainProbe().DetailFields;

        IReadOnlyDictionary<object, TextDefinition> byDefault = fields.Single(field => field.Name == nameof(PlainProbeResponse.Active)).ValueTexts!;
        Assert.Equal(ShellTexts.Values.Yes, byDefault[true]);
        Assert.Equal(ShellTexts.Values.No, byDefault[false]);

        IReadOnlyDictionary<object, TextDefinition> declared = fields.Single(field => field.Name == nameof(PlainProbeResponse.Enabled)).ValueTexts!;
        Assert.Equal(PlainProbeTexts.EnabledOn, declared[true]);
        Assert.Equal(PlainProbeTexts.EnabledOff, declared[false]);
    }

    private static AggregateViewModel PlainProbe() =>
        AggregateViewTestRegistry.Build(typeof(PlainProbeViewConfiguration)).For<PlainProbe>();

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

        Assert.Equal(typeof(GetViewProbeQuery), probe.GetOne!.Type);
        Assert.Equal(typeof(ListViewProbesQuery), probe.List!.Type);
        Assert.Equal(typeof(CreateViewProbeCommand), probe.Create!.Type);
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
        Assert.Equal(PartHolderTexts.ViewOwnerId.Label, part.ParentLabel!.Label);
        Assert.Equal(ShellTexts.Forms.SelectParentFirst, part.ParentFirst);
        Assert.True(part.Options!.IsChild);
        Assert.False(part.ParentOptions!.IsChild);

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
    public void ViewModel_SetPropertyLabel_DefaultsToFieldLabel()
    {
        IReadOnlyList<CommandView> commands = AggregateViewTestRegistry.Build(typeof(EditProbeViewConfiguration)).For<EditProbe>().Commands;
        CommandView retitle = commands.Single(command => command.Request == typeof(RetitleProbeCommand));
        CommandView color = commands.Single(command => command.Request == typeof(SetProbeColorCommand));
        CommandView rename = commands.Single(command => command.Request == typeof(RenameProbeCommand));

        Assert.Equal(EditProbeTexts.Title.Label, retitle.Label);
        Assert.Equal(SerginEditMode.Dialog, retitle.EditMode);
        Assert.Equal(SerginActionKind.Secondary, retitle.Kind);
        Assert.Equal(EditProbeTexts.Color.Label, color.Label);
        Assert.Equal(FormInputKind.Select, color.ValueField!.Kind);
        Assert.Equal(CommonTexts.Rename, rename.Label);
        Assert.Equal(SerginEditMode.Inline, rename.EditMode);
        Assert.Equal(FormInputKind.Text, rename.ValueField!.Kind);
        Assert.Equal([EditProbePermissions.Update], rename.Permissions);
        Assert.True(rename.RequiresVersion);
    }

    [Fact]
    public void Registry_RealHost_BuildsDeviceModel()
    {
        AggregateViewModel device = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Device>();

        Assert.Equal("dm", device.Schema);
        Assert.Equal(DeviceUiTexts.Title, device.Title);
        Assert.Equal("/dm/devices", device.Routes.List);
        Assert.Contains(device.Commands, command => command.Request == typeof(ReadDeviceProfileCommand));
        Assert.Contains(device.Commands, command => command.Request == typeof(DeleteDeviceCommand));
        Assert.True(device.HasSchedulableCommand);
        Assert.Equal(typeof(GetDeviceByIdQueryCommand), device.GetOne!.Type);
    }

    [Fact]
    public void Registry_Find_IsNullForAnUnconfiguredRoot()
    {
        AggregateViewRegistry registry = AggregateViewTestRegistry.Build(typeof(ViewProbeViewConfiguration));

        Assert.Null(registry.Find(typeof(ViewOwner)));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Registry_Manufacturer_HasDeviceModelChild()
    {
        ChildViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>().Children.Single();

        Assert.Equal(typeof(DeviceModel), model.Child);
        Assert.Equal(DeviceModelTexts.Singular, model.Singular);
        Assert.Equal(DeviceModelUiTexts.Title, model.Title);
        Assert.Equal(typeof(GetDeviceModelListQueryCommand), model.List!.Type);
        Assert.Equal(typeof(GetDeviceModelByIdQueryCommand), model.GetOne!.Type);
        Assert.Equal(typeof(AddDeviceModelCommand), model.Add!.Type);
        Assert.Equal(typeof(RenameDeviceModelCommand), model.Edit!.Type);
        Assert.Equal(typeof(RenameDeviceModelCommand), model.EditCommand!.Request);
        Assert.Equal(SerginEditMode.Page, model.EditCommand.EditMode);
        Assert.Equal(DeviceModelUiTexts.Empty, model.EmptyText);
        Assert.Equal(DeviceModelUiTexts.SearchPlaceholder, model.ListSearchPlaceholder);
        Assert.Equal(DeviceModelUiTexts.New, model.AddTitle);
        Assert.Equal(DeviceModelUiTexts.RenameTitle, model.EditTitle);
        Assert.Equal("Name", model.ListTitleField);
        Assert.Equal(["Name"], model.AddFields.Select(field => field.Name));
        Assert.Equal(["Name"], model.EditFields.Select(field => field.Name));

        var manufacturer = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        Assert.Equal($"/dm/manufacturers/{manufacturer}/models/{id}", model.Routes.Detail!(manufacturer, id));
    }

    [Fact]
    public void Registry_DeviceModelCommands_ReadPolicyFromConfiguration()
    {
        ChildViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>().Children.Single();
        CommandView add = model.Commands.Single(command => command.Request == typeof(AddDeviceModelCommand));
        CommandView remove = model.Commands.Single(command => command.Request == typeof(RemoveDeviceModelCommand));
        CommandView rename = model.Commands.Single(command => command.Request == typeof(RenameDeviceModelCommand));

        Assert.All([add, remove, rename], command =>
        {
            Assert.Equal([ManufacturerPermissions.Update], command.Permissions);
            Assert.True(command.AnyScope);
            Assert.True(command.RequiresVersion);
        });
        Assert.True(remove.Selectable);
        Assert.Equal(CommandRole.AddChild, add.Role);
        Assert.Equal(CommandRole.RemoveChild, remove.Role);
        Assert.Equal(CommandRole.SetProperty, rename.Role);
        Assert.Equal(DeviceModelUiTexts.Add, add.Label);
        Assert.Equal(CommonTexts.Remove, remove.Label);
        Assert.Equal(Icons.Material.Outlined.Delete, remove.Icon);
        Assert.Equal(SerginActionKind.Danger, remove.Kind);
        Assert.Equal(CommonTexts.Rename, rename.Label);
        Assert.Equal(Icons.Material.Outlined.Edit, rename.Icon);
        Assert.NotNull(remove.BuildFromIds);
        Assert.Equal(DeviceModelUiTexts.InUseTooltip, remove.DisabledReason);
        Assert.True(remove.DisabledWhen!(new GetDeviceModelListItem(Guid.Empty, "x", 1)));
        Assert.False(remove.DisabledWhen(new GetDeviceModelListItem(Guid.Empty, "x", 0)));
        Assert.Null(add.DisabledWhen);
    }

    [Fact]
    public void Registry_DeviceModelList_HasCountColumnWithFormat()
    {
        ChildViewModel model = factory.Services.GetRequiredService<AggregateViewRegistry>().For<Manufacturer>().Children.Single();
        FieldView count = model.ListFields.Single(field => field.Name == nameof(GetDeviceModelListItem.DeviceCount));

        Assert.Equal(DeviceModelUiTexts.DevicesColumn, count.Label);
        Assert.Equal(DeviceModelUiTexts.DeviceCount, count.Format);
        Assert.Equal("Count", count.FormatPlaceholder);
        Assert.Equal(DeviceModelTexts.Name.Label, model.ListFields.Single(field => field.Name == nameof(GetDeviceModelListItem.Name)).Label);
        Assert.Equal(DeviceModelUiTexts.Identifier, model.DetailFields.Single(field => field.Name == nameof(DeviceModelQueryResponse.Id)).Label);
    }

    [Fact]
    public void Registry_ChildHostProbe_BuildsItsChild()
    {
        ChildViewModel model = AggregateViewTestRegistry.Build(typeof(ChildHostViewConfiguration)).For<ChildHost>().Children.Single();

        Assert.Equal(typeof(ViewProbeChild), model.Child);
        Assert.Equal(typeof(ListViewProbeChildrenQuery), model.List!.Type);
        Assert.Equal(typeof(RenameViewProbeChildCommand), model.Edit!.Type);
        Assert.Equal(ViewProbeChildTexts.Add, model.Commands.Single(command => command.Role == CommandRole.AddChild).Label);
    }
}
