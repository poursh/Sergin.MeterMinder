using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceDetailPage
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
    private NavigationManager Navigation { get; set; } = default!;

    // A property, not a field: the tail is the page title's word until the load fills in the device id, and
    // on the not-found path it stays that way beside the problem panel.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        new(device?.DeviceId ?? "Device"),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Keeps the version the device was read at, so the delete can say which device it means.
    private async Task LoadAsync()
    {
        VersionedResult<DeviceQueryResponse> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(Id));

        if (loaded.Result.IsError)
        {
            device = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.Result.FirstError);

            return;
        }

        problem = null;
        device = loaded.Result.Value;
        version = loaded.Version;
    }

    private async Task DeleteAsync()
    {
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete device",
            $"Delete {device?.DeviceId}? It disappears from every list and page.",
            yesText: "Delete",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        deleting = true;

        VersionedResult<DeleteDeviceCommandResponse> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(Id), version);

        deleting = false;

        if (result.Result.IsError)
        {
            ErrorPresenter.Notify(result.Result.Errors);

            // Someone changed the device after this page loaded it: show what is there now, with its version.
            if (result.Result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo("/dm/devices");
    }
}
