using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

public sealed partial class DeviceDetailView
{
    private DeviceQueryResponse? device;
    private SerginProblem? problem;
    private bool deleting;
    private RowVersion? version;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [CascadingParameter]
    private SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;

    // A property, not a field: the tail is the page title's word until the load fills in the device id, and
    // on the not-found path it stays that way beside the problem panel.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        device is null ? new(DeviceTexts.Singular) : SerginBreadcrumb.Data(device.DeviceId),
    ];

    // Edit, then Delete at the far end; each hidden from a user who lacks its permission on the current scope.
    private IReadOnlyList<SerginPageAction> Actions =>
    [
        new(CommonTexts.Edit, Icons.Material.Outlined.Edit, SerginActionKind.Secondary, Href: $"/dm/devices/{Id}/edit", Permission: DevicePermissions.Update),
        new(CommonTexts.Delete, Icons.Material.Outlined.Delete, SerginActionKind.Danger, OnClick: EventCallback.Factory.Create(this, DeleteAsync),
            Permission: DevicePermissions.Delete, Disabled: deleting),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    private Task<ErrorOr<AuditInfo>> LoadAuditAsync() => Dispatcher.SendAsync(new GetDeviceAuditQueryCommand(new DeviceIntenralId(Id)));

    // Keeps the version the device was read at, so the delete can say which device it means.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(new DeviceIntenralId(Id)));

        if (loaded.IsError)
        {
            device = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        device = loaded.Value.Value;
        version = loaded.Value.Version;
    }

    private async Task DeleteAsync()
    {
        if (!await DialogService.ConfirmDeleteAsync(
                Localizer, DeviceUiTexts.DeleteTitle, DeviceUiTexts.DeleteConfirm, new { device?.DeviceId }))
        {
            return;
        }

        deleting = true;

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(new DeviceIntenralId(Id)), version);

        deleting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the device after this view loaded it: show what is there now, with its version.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        await Host.LeaveAsync("/dm/devices");
    }
}
