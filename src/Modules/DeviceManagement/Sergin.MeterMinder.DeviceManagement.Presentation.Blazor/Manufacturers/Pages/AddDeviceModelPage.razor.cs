using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class AddDeviceModelPage
{
    private readonly NewDeviceModelFormModel model = new();

    private bool submitting;

    [Parameter]
    public Guid ManufacturerId { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<AddDeviceModelCommandResponse> result = await Dispatcher.SendAsync(
            new AddDeviceModelCommand(new ManufacturerId(ManufacturerId), new DeviceModelName(model.Name)));

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: a duplicate name arrives as one validation error from the aggregate,
            // an unknown manufacturer as not-found from the handler.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}/models/{result.Value.Id}");
    }
}
