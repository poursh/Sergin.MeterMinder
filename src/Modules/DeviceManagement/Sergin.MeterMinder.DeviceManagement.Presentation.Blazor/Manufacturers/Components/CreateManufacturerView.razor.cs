using Microsoft.AspNetCore.Components;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Components;

public sealed partial class CreateManufacturerView
{
    private readonly NewManufacturerFormModel model = new();

    private bool submitting;
    private Func<object, string, Task<IEnumerable<string>>> validation = default!;

    private IReadOnlyList<SerginBreadcrumb> Trail { get; } =
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new("New manufacturer"),
    ];

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private ISerginFormValidator FormValidator { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [CascadingParameter]
    private SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;

    // Created on this view, so it re-renders after SubmitAsync; SerginFormView invokes it once every field is valid.
    private EventCallback OnSubmit => EventCallback.Factory.Create(this, SubmitAsync);

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    // A blank address means "no address": the validator refuses an empty non-null Address, so the
    // field has to become null rather than an empty ManufacturerAddress.
    private CreateManufacturerCommand ToCommand() => new(
        new ManufacturerName(model.Name),
        string.IsNullOrWhiteSpace(model.Address) ? null : new ManufacturerAddress(model.Address));

    private async Task SubmitAsync()
    {
        submitting = true;

        ErrorOr<CreateManufacturerCommandResponse> result = await Dispatcher.SendAsync(ToCommand());

        submitting = false;

        if (result.IsError)
        {
            // Every error, not the first: validation yields one per broken rule.
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        await Host.LeaveAsync($"/dm/manufacturers/{result.Value.Id}");
    }
}
