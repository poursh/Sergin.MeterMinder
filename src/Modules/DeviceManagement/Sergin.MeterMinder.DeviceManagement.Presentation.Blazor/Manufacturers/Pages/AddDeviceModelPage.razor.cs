using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.AddDeviceModel;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class AddDeviceModelPage
{
    private readonly NewDeviceModelFormModel model = new();

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

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

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
