using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class DeviceModelDetailPage
{
    private DeviceModelQueryResponse? deviceModel;
    private SerginProblem? problem;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    // A property, not a field: both tail steps are the page title's words until the load fills them in, and
    // on the not-found path they stay that way beside the problem panel. The manufacturer step links by the
    // route parameter, not the read model's id, so it is a link before the load too.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(deviceModel?.ManufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}"),
        new(deviceModel?.Name ?? "Device model"),
    ];

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<DeviceModelQueryResponse> result =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(ManufacturerId, Id));

        if (result.IsError)
        {
            deviceModel = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        deviceModel = result.Value;
    }
}
