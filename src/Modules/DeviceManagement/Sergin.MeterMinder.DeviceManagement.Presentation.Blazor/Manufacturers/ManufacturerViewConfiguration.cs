using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Components;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Localizations.Common;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers;

/// <summary>
/// The manufacturer's generated views: its routes, titles, the labels its commands cannot imply and its device models,
/// a child entity with its own routes, commands, fields and forms.
/// </summary>
internal sealed class ManufacturerViewConfiguration : IAggregateViewConfiguration<Manufacturer>
{
    public void Configure(AggregateViewBuilder<Manufacturer> view)
    {
        view.Title(ManufacturerUiTexts.Title)
            .Routes("/dm/manufacturers", id => $"/dm/manufacturers/{id}", "/dm/manufacturers/new", id => $"/dm/manufacturers/{id}/edit")
            .DetailOverviewCollapsed();
        view.Slot<ManufacturerOverviewSlot>(SerginViewSlot.DetailOverview).Slot<ManufacturerOverviewSummarySlot>(SerginViewSlot.DetailOverviewSummary);

        view.Command<CreateManufacturerCommand>().Label(ManufacturerUiTexts.New);
        view.Command<DeleteManufacturerCommand>()
            .ConfirmDelete(ManufacturerUiTexts.DeleteTitle, ManufacturerUiTexts.DeleteConfirm)
            .ConfirmDeleteMany(ManufacturerUiTexts.DeleteManyTitle, ManufacturerUiTexts.DeleteManyConfirm);

        view.Detail<ManufacturerQueryResponse>(detail => detail.Title(x => x.Name));
        view.List<GetManufacturerListItem>(list => list.Title(x => x.Name).SearchPlaceholder(ManufacturerUiTexts.SearchPlaceholder));
        view.Form<CreateManufacturerCommand>(form => form.Title(ManufacturerUiTexts.New));
        view.Form<UpdateManufacturerCommand>(form => form.Title(ManufacturerUiTexts.EditTitle));

        view.Child<DeviceModel>(child =>
        {
            child.Named(DeviceModelTexts.Singular).Title(DeviceModelUiTexts.Title)
                .Routes(
                    detail: (manufacturerId, id) => $"/dm/manufacturers/{manufacturerId}/models/{id}",
                    create: manufacturerId => $"/dm/manufacturers/{manufacturerId}/models/new",
                    edit: (manufacturerId, id) => $"/dm/manufacturers/{manufacturerId}/models/{id}/edit");

            child.Command<AddDeviceModelCommand>().Label(DeviceModelUiTexts.Add);
            child.Command<RenameDeviceModelCommand>().Label(CommonTexts.Rename).Icon(Icons.Material.Outlined.Edit).Edit(SerginEditMode.Page);
            child.Command<RemoveDeviceModelCommand>()
                .ConfirmDelete(DeviceModelUiTexts.RemoveTitle, DeviceModelUiTexts.RemoveConfirm)
                .ConfirmDeleteMany(DeviceModelUiTexts.RemoveManyTitle, DeviceModelUiTexts.RemoveManyConfirm)
                .DisabledWhen<GetDeviceModelListItem>(x => x.DeviceCount > 0, DeviceModelUiTexts.InUseTooltip);

            child.List<GetDeviceModelListItem>(list =>
            {
                list.Title(x => x.Name).SearchPlaceholder(DeviceModelUiTexts.SearchPlaceholder).Empty(DeviceModelUiTexts.Empty);
                list.Field(x => x.DeviceCount).Label(DeviceModelUiTexts.DevicesColumn).Format(DeviceModelUiTexts.DeviceCount, "Count");
            });
            child.Detail<DeviceModelQueryResponse>(detail =>
            {
                detail.Title(x => x.Name);
                detail.Field(x => x.ManufacturerName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}");
                detail.Field(x => x.Id).Label(DeviceModelUiTexts.Identifier);
            });
            child.Form<AddDeviceModelCommand>(form => form.Title(DeviceModelUiTexts.New));
            child.Form<RenameDeviceModelCommand>(form => form.Title(DeviceModelUiTexts.RenameTitle));
        });
    }
}
