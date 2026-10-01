using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class EditDevicePage
{
    private readonly EditDeviceFormModel model = new();

    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private IReadOnlyCollection<GetDeviceModelListItem> deviceModels = [];

    // Page state only: the manufacturer narrows the model picker and is not part of the command.
    private Guid selectedManufacturerId;

    // The device id as loaded, for the trail: model.DeviceId changes as the user types.
    private string? loadedDeviceId;
    private RowVersion? version;
    private SerginProblem? problem;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid Id { get; set; }

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

    // A property, not a field: the record step reads the loaded device id. It links by the route id, so it is a
    // link on the not-found path too.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        new(loadedDeviceId ?? "Device", $"/dm/devices/{Id}"),
        new("Edit"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Fills the form from the stored device and keeps the version it was read at. Called again after a stale
    // save, so the next submit is against what is there now.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(Id));

        if (loaded.IsError)
        {
            version = null;
            loadedDeviceId = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        DeviceQueryResponse device = loaded.Value.Value;
        version = loaded.Value.Version;
        loadedDeviceId = device.DeviceId;
        model.DeviceId = device.DeviceId;

        await LoadManufacturersAsync();
        await LoadModelsAsync(device.ManufacturerId);
        model.DeviceModelId = device.DeviceModelId;
    }

    private async Task LoadManufacturersAsync()
    {
        // One page of 200, as on CreateDevicePage: there is no server-side search to fall back on.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendAsync(new GetManufacturerListQueryCommand(Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        manufacturers = result.Value.Data;
    }

    private async Task OnManufacturerChangedAsync(Guid manufacturerId)
    {
        model.DeviceModelId = Guid.Empty;
        await LoadModelsAsync(manufacturerId);
    }

    private async Task LoadModelsAsync(Guid manufacturerId)
    {
        selectedManufacturerId = manufacturerId;
        deviceModels = [];

        if (manufacturerId == Guid.Empty)
        {
            return;
        }

        // Same 200-row caveat as the manufacturer picker above.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result = await Dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(new ManufacturerId(manufacturerId), Paggination.Create(200, 1)));

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return;
        }

        deviceModels = result.Value.Data;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift. The id is in
    // it so the uniqueness rule can leave this device out.
    private UpdateDeviceCommand ToCommand() =>
        new(Id, new DeviceId(model.DeviceId), new DeviceModelInternalId(model.DeviceModelId));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || version is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<UpdateDeviceCommandResponse>> result = await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the device after this page loaded it: show what is there now, with its version.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/devices/{Id}");
    }
}
