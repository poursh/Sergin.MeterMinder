using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Components;

public sealed partial class RenameDeviceModelView
{
    private readonly RenameDeviceModelFormModel model = new();

    private string? manufacturerName;
    private string? loadedModelName;

    // The model has no version of its own: a rename changes the Manufacturer aggregate, so it is guarded by the
    // manufacturer's version.
    private RowVersion? manufacturerVersion;
    private SerginProblem? problem;

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
    private IStringLocalizer Localizer { get; set; } = default!;

    [CascadingParameter]
    private SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;

    private string ModelHref => $"/dm/manufacturers/{ManufacturerId}/models/{Id}";

    // Created on this view, so it re-renders after SubmitAsync; SerginFormView invokes it once every field is valid.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        manufacturerName is null
            ? new(ManufacturerTexts.Singular, $"/dm/manufacturers/{ManufacturerId}")
            : SerginBreadcrumb.Data(manufacturerName, $"/dm/manufacturers/{ManufacturerId}"),
        loadedModelName is null ? new(DeviceModelTexts.Singular, ModelHref) : SerginBreadcrumb.Data(loadedModelName, ModelHref),
        new(CommonTexts.Rename),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Two reads: the manufacturer for the version the rename is guarded by, then the model for its current name.
    // Version first: a change to the model between the two reads then makes the version stale, so the submit is
    // refused instead of overwriting a name this view never showed.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loadedManufacturer =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(new ManufacturerId(ManufacturerId)));
        ErrorOr<DeviceModelQueryResponse> loadedModel =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(new ManufacturerId(ManufacturerId), new DeviceModelInternalId(Id)));

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
        new(new ManufacturerId(ManufacturerId), new DeviceModelInternalId(Id), new DeviceModelName(model.Name));

    private async Task SubmitAsync()
    {
        if (manufacturerVersion is not { } expectedVersion)
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

        await Host.LeaveAsync(ModelHref);
    }
}
