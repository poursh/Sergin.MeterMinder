using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Pages;

public sealed partial class AddDeviceModelPage
{
    private readonly NewDeviceModelFormModel model = new();

    private string? manufacturerName;

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
    /// Loads the manufacturer only to name it in the trail. A failure is deliberately silent: the step keeps
    /// its placeholder, and an unknown manufacturer is already reported by the submit as not-found — a
    /// breadcrumb label is not worth a second snackbar.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<ManufacturerQueryResponse> result =
            await Dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(ManufacturerId));

        manufacturerName = result.IsError ? null : result.Value.Name;
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

        submitting = true;

        ErrorOr<AddDeviceModelCommandResponse> result = await Dispatcher.SendAsync(ToCommand());

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
