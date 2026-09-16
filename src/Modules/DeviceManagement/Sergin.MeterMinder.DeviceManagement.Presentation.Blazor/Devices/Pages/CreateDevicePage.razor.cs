using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class CreateDevicePage
{
    private readonly NewDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private IReadOnlyCollection<GetDeviceModelListItem> deviceModels = [];

    // Page state only: the manufacturer narrows the model picker and is not part of the command.
    private Guid selectedManufacturerId;
    private bool submitting;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        // One page of 200 fills the picker. There is no server-side search to fall back on
        // (the list repositories ignore Term/Filtering/Sorting), so beyond 200 manufacturers
        // the tail is silently unreachable here and this needs an autocomplete instead.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendAsync(
                new GetManufacturerListQueryCommand(Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task OnManufacturerChangedAsync(Guid manufacturerId)
    {
        selectedManufacturerId = manufacturerId;
        model.DeviceModelId = Guid.Empty;
        deviceModels = [];

        if (manufacturerId == Guid.Empty)
        {
            return;
        }

        // Same 200-row caveat as the manufacturer picker above.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceModelListQueryCommand(new ManufacturerId(manufacturerId), Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        deviceModels = result.Value.Data;
    }

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateDeviceCommandResponse> result = await Dispatcher.SendAsync(
            new CreateDeviceCommand(new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId)));

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo($"/dm/devices/{result.Value.Id}");
    }
}
