using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.Application.Devices.Commands.Create;
using Sergin.MeterMinder.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.Domain.Devices;
using Sergin.MeterMinder.Domain.Manufacturers;
using Sergin.MeterMinder.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.Presentation.Blazor.Devices.Pages;

public sealed partial class CreateDevicePage
{
    private readonly NewDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private bool submitting;

    [Inject]
    private ISerginUiDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendListAsync<GetManufacturerListItem>(200, 1);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateDeviceCommandResponse> result = await Dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(model.DeviceId), new ManufacturerId(model.ManufacturerId)));

        submitting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        Navigation.NavigateTo($"/mm/devices/{result.Value.Id}");
    }
}
