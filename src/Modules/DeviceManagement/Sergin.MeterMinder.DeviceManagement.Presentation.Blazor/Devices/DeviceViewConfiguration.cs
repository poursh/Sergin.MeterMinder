using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices;

/// <summary>The device's generated views: its routes, titles, the labels its commands cannot imply, and field links.</summary>
internal sealed class DeviceViewConfiguration : IAggregateViewConfiguration<Device>
{
    public void Configure(AggregateViewBuilder<Device> view)
    {
        view.Title(DeviceUiTexts.Title)
            .Routes("/dm/devices", id => $"/dm/devices/{id}", "/dm/devices/new", id => $"/dm/devices/{id}/edit");

        view.Command<CreateDeviceCommand>().Label(DeviceUiTexts.New);

        view.Command<ReadDeviceProfileCommand>()
            .Label(DeviceUiTexts.ReadProfile)
            .Icon(Icons.Material.Outlined.Sensors)
            .SucceededText(DeviceUiTexts.ReadProfileDone)
            .BulkSucceededText(DeviceUiTexts.BulkReadProfileDone);

        view.Command<DeleteDeviceCommand>()
            .ConfirmDelete(DeviceUiTexts.DeleteTitle, DeviceUiTexts.DeleteConfirm)
            .ConfirmDeleteMany(DeviceUiTexts.DeleteManyTitle, DeviceUiTexts.DeleteManyConfirm);

        view.Slot<DeviceOverviewSlot>(SerginViewSlot.ListOverview)
            .Slot<DeviceOverviewSummarySlot>(SerginViewSlot.ListOverviewSummary);

        view.Detail<DeviceQueryResponse>(detail =>
        {
            detail.Title(x => x.DeviceId);
            detail.Field(x => x.DeviceModelName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}/models/{x.DeviceModelId}");
            detail.Field(x => x.ManufacturerName).Link(x => $"/dm/manufacturers/{x.ManufacturerId}");
        });

        // A device's model is a child of its manufacturer: a manufacturer picker, then that manufacturer's models.
        view.Form<CreateDeviceCommand>(form => form.ChildPicker<Manufacturer, DeviceQueryResponse>(x => x.DeviceModelId, r => r.ManufacturerId));
        view.Form<UpdateDeviceCommand>(form => form.ChildPicker<Manufacturer, DeviceQueryResponse>(x => x.DeviceModelId, r => r.ManufacturerId));

        // Scope has no list query, so the scope filter is supplied.
        view.List<GetDeviceListItem>(list =>
        {
            list.Title(x => x.DeviceId);
            list.Filter(DeviceListFields.ScopeId, typeof(DeviceScopeFilter)).SearchPlaceholder(DeviceUiTexts.SearchPlaceholder);
        });
    }
}
