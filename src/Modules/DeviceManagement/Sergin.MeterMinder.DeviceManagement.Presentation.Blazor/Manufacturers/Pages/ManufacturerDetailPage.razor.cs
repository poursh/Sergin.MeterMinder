using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerDetailPage
{
    private ManufacturerQueryResponse? manufacturer;
    private SerginProblem? problem;
    private bool deleting;
    private RowVersion? version;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    // A property, not a field: the tail is the page title's word until the load fills in the name, and on
    // the not-found path it stays that way beside the problem panel.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        new(manufacturer?.Name ?? "Manufacturer"),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    // Keeps the version the manufacturer was read at, so the delete can say which manufacturer it means.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(Id));

        if (loaded.IsError)
        {
            manufacturer = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        manufacturer = loaded.Value.Value;
        version = loaded.Value.Version;
    }

    private async Task DeleteAsync()
    {
        bool? confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete manufacturer",
            $"Delete {manufacturer?.Name} and its models? They disappear from every list and page.",
            yesText: "Delete",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        deleting = true;

        ErrorOr<Versioned<DeleteManufacturerCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteManufacturerCommand(Id), version);

        deleting = false;

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

        Navigation.NavigateTo("/dm/manufacturers");
    }
}
