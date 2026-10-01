using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class RenameDeviceModelPage
{
    private readonly RenameDeviceModelFormModel model = new();

    private string? manufacturerName;
    private string? loadedModelName;

    // The model has no version of its own: a rename changes the Manufacturer aggregate, so it is guarded by the
    // manufacturer's version.
    private RowVersion? manufacturerVersion;
    private SerginProblem? problem;

    private MudForm form = default!;
    private bool isValid;
    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    [Parameter]
    public Guid ManufacturerId { get; set; }

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

    private string ModelHref => $"/dm/manufacturers/{ManufacturerId}/models/{Id}";

    // An EventCallback, not a bare delegate: splatted onto MudForm's <form> it gets a receiver, so the
    // page re-renders after SubmitAsync.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(manufacturerName ?? "Manufacturer", $"/dm/manufacturers/{ManufacturerId}"),
        new(loadedModelName ?? "Device model", ModelHref),
        new("Rename"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Two reads: the manufacturer for the version the rename is guarded by, then the model for its current name.
    // Version first: a change to the model between the two reads then makes the version stale, so the submit is
    // refused instead of overwriting a name this page never showed.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loadedManufacturer =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));
        ErrorOr<DeviceModelQueryResponse> loadedModel =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(ManufacturerId, Id));

        if (loadedModel.IsError || loadedManufacturer.IsError)
        {
            manufacturerVersion = null;
            loadedModelName = null;
            manufacturerName = null;
            problem = ErrorPresenter.Present(loadedModel.IsError ? loadedModel.FirstError : loadedManufacturer.FirstError);

            return;
        }

        problem = null;
        manufacturerVersion = loadedManufacturer.Value.Version;
        manufacturerName = loadedModel.Value.ManufacturerName;
        loadedModelName = loadedModel.Value.Name;
        model.Name = loadedModel.Value.Name;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    private RenameDeviceModelCommand ToCommand() =>
        new(new ManufacturerId(ManufacturerId), Id, new DeviceModelName(model.Name));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || manufacturerVersion is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<RenameDeviceModelCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            // A duplicate name arrives as one validation error from the aggregate, a stale manufacturer as a
            // version error.
            ErrorPresenter.Notify(result.Errors);

            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo(ModelHref);
    }
}
