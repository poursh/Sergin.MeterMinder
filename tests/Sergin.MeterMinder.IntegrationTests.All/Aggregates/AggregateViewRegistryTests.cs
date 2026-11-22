using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Runtime;
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
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Actions;

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
    public void ViewModel_ListUnder_HasParentAndRoute()
    {
        AggregateViewModel model = AggregateViewTestRegistry
            .Build(typeof(ViewProbeViewConfiguration), typeof(ViewProbeNoteViewConfiguration))
            .For<ViewProbeNote>();
        var probe = Guid.CreateVersion7();

        Assert.Equal(typeof(ViewProbe), model.Parent);
        Assert.Null(model.Routes.List);
        Assert.Equal($"/tst/view-probes/{probe}/notes", model.Routes.ListUnderParent!(probe));
        Assert.Equal(typeof(ViewProbeNoteListSlot), model.Slots[SerginViewSlot.ListBody]);

        // Every list query the view sends carries the parent's id.
        var query = (ListViewProbeNotesQuery)model.List!.Build(new ListQueryInput(Paggination.Create(10, 1), "note", [], [], probe));

        Assert.Equal(new ViewProbeId(probe), query.ParentId);
        Assert.Equal("note", query.Term?.Value);

        // Its own optional parameters take their declared defaults.
        Assert.Null(query.Tag);
        Assert.Equal(7, query.Limit);
        Assert.Equal(default, query.Since);
        Assert.Equal(Guid.Empty, query.Marker);
    }

    [Fact]
    public void Registry_ReadsPermissionScopeVersionAndSchedule_FromCommandConfiguration()
    {
        CommandView run = Probe().Commands.Single(command => command.Request == typeof(RunViewProbeCommand));

        Assert.Equal(CommandRole.Task, run.Role);
        Assert.Equal([ViewProbePermissions.Run], run.Permissions);
        Assert.True(run.AnyScope);
        Assert.False(run.RequiresVersion);
        Assert.Equal("tst.view-probes.run", run.Task?.Name);
        Assert.True(run.Task?.Schedulable);
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
    public void Registry_DeleteWithExtraValues_HasConfirmFields()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ConfirmProbeViewConfiguration)).For<ConfirmProbe>();
        CommandView delete = model.Commands.Single(command => command.Request == typeof(DeleteConfirmProbeCommand));
        var id = Guid.CreateVersion7();

        FormFieldView cascade = Assert.Single(delete.ConfirmFields);
        Assert.Equal(nameof(DeleteConfirmProbeCommand.Cascade), cascade.Name);
        Assert.Equal(FormInputKind.Switch, cascade.Kind);
        Assert.Equal(ConfirmProbeTexts.Cascade.Label, cascade.Label.Label);
        Assert.Null(delete.BuildFromId);
        Assert.Null(delete.ValueField);

        object built = delete.BuildFromValues!(id, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(DeleteConfirmProbeCommand.Cascade)] = true });

        Assert.Equal(new DeleteConfirmProbeCommand(new ConfirmProbeId(id), Cascade: true), built);
        Assert.True(delete.RequiresVersion);
    }

    [Fact]
    public void Registry_Confirm_SetsTitleTextAndLabel()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ConfirmProbeViewConfiguration)).For<ConfirmProbe>();
        CommandView archive = model.Commands.Single(command => command.Request == typeof(ArchiveConfirmProbeCommand));
        CommandView delete = model.Commands.Single(command => command.Request == typeof(DeleteConfirmProbeCommand));

        Assert.Equal(ConfirmProbeTexts.ArchiveTitle, archive.ConfirmTitle);
        Assert.Equal(ConfirmProbeTexts.ArchiveText, archive.ConfirmText);
        Assert.Equal(ConfirmProbeTexts.ArchiveConfirm, archive.ConfirmLabel);
        Assert.Empty(archive.ConfirmFields);
        Assert.NotNull(archive.BuildFromId);

        // ConfirmDelete sets no label: the confirm button is the command's own label.
        Assert.Equal(ConfirmProbeTexts.DeleteText, delete.ConfirmText);
        Assert.Null(delete.ConfirmLabel);
    }

    [Fact]
    public void ViewModel_ReversePair_PartnerIsNegation()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(PublishProbeViewConfiguration)).For<PublishProbe>();
        CommandView publish = model.Commands.Single(command => command.Request == typeof(PublishProbeCommand));
        CommandView unpublish = model.Commands.Single(command => command.Request == typeof(UnpublishProbeCommand));
        var draft = new PublishProbeResponse(Guid.CreateVersion7(), "Draft", Published: false);
        PublishProbeResponse live = draft with { Published = true };

        Assert.True(publish.AppliesWhen!(draft));
        Assert.False(publish.AppliesWhen(live));
        Assert.False(unpublish.AppliesWhen!(draft));
        Assert.True(unpublish.AppliesWhen(live));

        // A record that is not the declared one (a list row) satisfies neither side.
        var row = new PublishProbeListItem(draft.Id, "Draft", Published: false);
        Assert.False(publish.AppliesWhen(row));
        Assert.False(unpublish.AppliesWhen(row));
    }

    [Fact]
    public void ViewModel_Kind_ReplacesTheRolesDefault()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(PublishProbeViewConfiguration)).For<PublishProbe>();

        Assert.Equal(SerginActionKind.Primary, model.Commands.Single(command => command.Request == typeof(PublishProbeCommand)).Kind);
        Assert.Equal(SerginActionKind.Secondary, model.Commands.Single(command => command.Request == typeof(UnpublishProbeCommand)).Kind);
    }

    [Fact]
    public void ViewModel_Link_IsCollected()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(PublishProbeViewConfiguration)).For<PublishProbe>();
        var id = Guid.CreateVersion7();

        ViewLinkView link = Assert.Single(model.Links!);
        Assert.Equal(PublishProbeTexts.Translations, link.Label);
        Assert.Equal(MudBlazor.Icons.Material.Outlined.Translate, link.Icon);
        Assert.Equal($"/tst/publish-probes/{id}/translations", link.Href(id));
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

    [Fact]
    public void Registry_ChildSection_SkipsListAndCommands()
    {
        // The tags' list query is unpaged and their add command's Label has no field text: a generated child would be
        // refused for either, a Section child builds neither.
        ChildViewModel tags = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>().Children.Single();

        Assert.Equal(typeof(ParentProbeTag), tags.Child);
        Assert.Equal(typeof(ParentProbeTagsSection), tags.Section);
        Assert.Equal(ParentProbeTexts.Tag, tags.Singular);
        Assert.Equal(ChildViewRoutes.None, tags.Routes);
        Assert.Null(tags.List);
        Assert.Null(tags.ListBodyType);
        Assert.Null(tags.GetOne);
        Assert.Null(tags.Add);
        Assert.Null(tags.Edit);
        Assert.Empty(tags.Commands);
    }

    [Fact]
    public void Registry_Move_ReadsTheTreeAndBuildsFromIdAndParent()
    {
        AggregateViewModel model = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>();
        CommandView move = model.Commands.Single(command => command.Request == typeof(MoveParentProbeCommand));
        var id = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();

        // The move: a Secondary detail action with its label, the parent picker labelled by its NewParentId field text.
        Assert.Equal(CommandRole.Move, move.Role);
        Assert.Equal(SerginActionKind.Secondary, move.Kind);
        Assert.Equal(ParentProbeTexts.Move, move.Label);
        Assert.Equal(ParentProbeTexts.NewParentId, move.MoveParentField);
        Assert.Equal(new MoveParentProbeCommand(new ParentProbeId(id), new ParentProbeId(parent)), move.BuildMove!(id, parent));

        // The tree: its query and item, read through the compiled readers, titled as configured.
        TreeView tree = model.Tree!;
        var item = new ParentProbeTreeItem(id, parent, "Treed");
        Assert.Equal(typeof(GetParentProbeTreeQuery), tree.Query.Type);
        Assert.IsType<GetParentProbeTreeQuery>(tree.Query.Build());
        Assert.Equal(typeof(ParentProbeTreeItem), tree.Item);
        Assert.Equal(id, tree.IdOf(item));
        Assert.Equal(parent, tree.ParentIdOf(item));
        Assert.Null(tree.ParentIdOf(item with { ParentId = null }));
        Assert.Equal("Treed", tree.TitleOf(item));
    }

    [Fact]
    public void Tree_CarriesTheDeclaredIconAndTexts()
    {
        TreeView tree = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>().Tree!;

        Assert.Equal("root-icon", tree.IconOf!(new ParentProbeTreeItem(Guid.NewGuid(), null, "a")));
        Assert.Equal("node-icon", tree.IconOf!(new ParentProbeTreeItem(Guid.NewGuid(), Guid.NewGuid(), "b")));
        Assert.Same(ParentProbeTexts.Filter, tree.FilterPlaceholder);
        Assert.Same(ParentProbeTexts.Prompt, tree.PromptTitle);
        Assert.Same(ParentProbeTexts.PromptHint, tree.PromptHint);
        Assert.Equal("prompt-icon", tree.PromptIcon);
        Assert.Same(ParentProbeTexts.Kids, tree.ChildrenTitle);
        Assert.Same(ParentProbeTexts.NoKids, tree.ChildrenEmpty);
        Assert.Same(ShellTexts.Trees.NothingToShow, tree.Empty);
    }

    [Fact]
    public void Tree_WithOnlyATitle_TakesTheCommonDefaults()
    {
        TreeView tree = AggregateViewTestRegistry.Build(typeof(PlainTreeProbeViewConfiguration)).For<ParentProbe>().Tree!;

        Assert.Null(tree.IconOf);
        Assert.Same(ShellTexts.Trees.FilterPlaceholder, tree.FilterPlaceholder);
        Assert.Same(ShellTexts.Trees.SelectPrompt, tree.PromptTitle);
        Assert.Null(tree.PromptHint);
        Assert.Equal(Icons.Material.Outlined.AccountTree, tree.PromptIcon);
        Assert.Same(ShellTexts.Trees.Children, tree.ChildrenTitle);
        Assert.Same(ShellTexts.Trees.NoChildren, tree.ChildrenEmpty);
        Assert.Same(ShellTexts.Trees.NothingToShow, tree.Empty);
    }

    [Fact]
    public void DetailField_WhenEmpty_IsCarriedOnTheFieldView()
    {
        FieldView field = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>()
            .DetailFields.Single(f => f.Name == "ParentName");

        Assert.Same(ParentProbeTexts.NoParent, field.WhenEmpty);
    }

    [Fact]
    public void MoveCandidates_ExcludeSelfAndDescendants()
    {
        TreeView tree = AggregateViewTestRegistry.Build(typeof(ParentProbeViewConfiguration)).For<ParentProbe>().Tree!;
        var root = Guid.CreateVersion7();
        var a = Guid.CreateVersion7();
        var a1 = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();

        // Out of order on purpose: root > a > a1, root > c, root > b.
        IReadOnlyCollection<object> items =
        [
            new ParentProbeTreeItem(a1, a, "a1"),
            new ParentProbeTreeItem(c, root, "c"),
            new ParentProbeTreeItem(root, null, "root"),
            new ParentProbeTreeItem(a, root, "a"),
            new ParentProbeTreeItem(b, root, "b"),
        ];

        // Moving a: neither a nor a1, the rest depth first with siblings by title, the root at depth 0.
        Assert.Equal([(root, "root", 0), (b, "b", 1), (c, "c", 1)], MoveCandidates.For(tree, items, a));

        // Moving a1 leaves a offered, with its depth.
        Assert.Equal([(root, "root", 0), (a, "a", 1), (b, "b", 1), (c, "c", 1)], MoveCandidates.For(tree, items, a1));
    }
}
