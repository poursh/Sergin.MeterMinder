using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceDetailPage
{
    private DeviceQueryResponse? device;
    private SerginProblem? problem;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<DeviceQueryResponse> result = await Dispatcher.SendAsync(new GetDeviceByIdQueryCommand(Id));

        if (result.IsError)
        {
            device = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        device = result.Value;
    }
}
