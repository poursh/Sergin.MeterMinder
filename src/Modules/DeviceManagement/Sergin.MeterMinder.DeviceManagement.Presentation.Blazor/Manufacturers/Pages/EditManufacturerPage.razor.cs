using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Models;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class EditManufacturerPage
{
    private readonly EditManufacturerFormModel model = new();

    // The name as loaded, for the trail: model.Name changes as the user types.
    private string? loadedName;
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

    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(loadedName ?? "Manufacturer", $"/dm/manufacturers/{Id}"),
        new("Edit"),
    ];

    protected override void OnInitialized() => validation = FormValidator.RulesFor(ToCommand);

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Id));

        if (loaded.IsError)
        {
            version = null;
            loadedName = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        version = loaded.Value.Version;
        loadedName = loaded.Value.Value.Name;
        model.Name = loaded.Value.Value.Name;
        model.Address = loaded.Value.Value.Address;
    }

    // One mapping for both the field-by-field validation and the submit, so the two cannot drift.
    // A blank address means "no address": the validator refuses an empty non-null Address.
    private UpdateManufacturerCommand ToCommand() => new(
        Id,
        new ManufacturerName(model.Name),
        string.IsNullOrWhiteSpace(model.Address) ? null : new ManufacturerAddress(model.Address));

    private async Task SubmitAsync()
    {
        await form.ValidateAsync();

        if (!form.IsValid || version is not { } expectedVersion)
        {
            return;
        }

        submitting = true;

        ErrorOr<Versioned<UpdateManufacturerCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(ToCommand(), expectedVersion);

        submitting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the manufacturer (added a model, say) after this page loaded it.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        Navigation.NavigateTo($"/dm/manufacturers/{Id}");
    }
}
