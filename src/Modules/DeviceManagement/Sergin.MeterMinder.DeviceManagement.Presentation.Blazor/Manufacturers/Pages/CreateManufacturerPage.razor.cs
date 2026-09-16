using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class CreateManufacturerPage
{
    private readonly NewManufacturerFormModel model = new();

    private bool submitting;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        submitting = true;

        // A blank address means "no address": the validator refuses an empty non-null Address, so the
        // field has to become null rather than an empty ManufacturerAddress.
        ManufacturerAddress? address = string.IsNullOrWhiteSpace(model.Address)
            ? null
            : new ManufacturerAddress(model.Address);

        ErrorOr<CreateManufacturerCommandResponse> result = await Dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName(model.Name), address));

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{result.Value.Id}");
    }
}
