using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
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

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    private IReadOnlyList<SerginBreadcrumb> Trail { get; } =
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        new("New device"),
    ];

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private ISerginFormValidator FormValidator { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so the
    // page re-renders after SubmitAsync.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    protected override async Task OnInitializedAsync()
    {
        validation = FormValidator.RulesFor(ToCommand);

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

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    private CreateDeviceCommand ToCommand() =>
        new(new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid)
        {
            return;
        }

        submitting = true;

        ErrorOr<CreateDeviceCommandResponse> result = await Dispatcher.SendAsync(ToCommand());

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
