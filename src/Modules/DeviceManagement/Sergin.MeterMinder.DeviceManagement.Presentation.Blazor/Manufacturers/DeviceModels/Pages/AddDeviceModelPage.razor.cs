using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class AddDeviceModelPage
{
    private readonly NewDeviceModelFormModel model = new();

    private string? manufacturerName;
    private RowVersion? manufacturerVersion;
    private IReadOnlyList<Error>? manufacturerLoadErrors;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid ManufacturerId { get; set; }

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

    // A property, not a field: the manufacturer step reads its name once the load below fills it in.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(manufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}"),
        new("New device model"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    /// <summary>
    /// Loads the manufacturer to name it in the trail and to keep the version the new model is added against. A
    /// failure keeps the trail's placeholder silently — a breadcrumb label is not worth a second snackbar — but
    /// its errors are kept for the submit, which has no version to send in that case.
    /// </summary>
    protected override Task OnParametersSetAsync() => LoadManufacturerAsync();

    private async Task LoadManufacturerAsync()
    {
        VersionedResult<ManufacturerQueryResponse> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));

        manufacturerName = loaded.Result.IsError ? null : loaded.Result.Value.Name;
        manufacturerVersion = loaded.Version;
        manufacturerLoadErrors = loaded.Result.IsError ? loaded.Result.Errors : null;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    private AddDeviceModelCommand ToCommand() =>
        new(new ManufacturerId(ManufacturerId), new DeviceModelName(model.Name));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid)
        {
            return;
        }

        // A manufacturer that could not be read has no version to send: sending a fabricated one would always
        // come back stale, so report the load failure instead and let the user retry.
        if (manufacturerVersion is not { } expectedVersion)
        {
            ErrorPresenter.Notify(manufacturerLoadErrors ?? [Error.NotFound()]);
            await LoadManufacturerAsync();
            return;
        }

        submitting = true;

        VersionedResult<AddDeviceModelCommandResponse> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.Result.IsError)
        {
            // Every error, not the first: a duplicate name arrives as one validation error from the aggregate,
            // an unknown manufacturer as not-found from the handler, a stale manufacturer as a version error.
            ErrorPresenter.Notify(result.Result.Errors);

            if (result.Result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadManufacturerAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}/models/{result.Result.Value.Id}");
    }
}
