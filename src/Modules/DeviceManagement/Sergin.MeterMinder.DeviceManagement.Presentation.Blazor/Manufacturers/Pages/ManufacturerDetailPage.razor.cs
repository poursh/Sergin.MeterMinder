using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerDetailPage
{
    private ManufacturerQueryResponse? manufacturer;
    private SerginProblem? problem;
    private bool deleting;

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

    protected override async Task OnParametersSetAsync()
    {
        ErrorOr<ManufacturerQueryResponse> result = await Dispatcher.SendAsync(new GetManufacturerByIdQueryCommand(Id));

        if (result.IsError)
        {
            manufacturer = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        manufacturer = result.Value;
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

        ErrorOr<DeleteManufacturerCommandResponse> result = await Dispatcher.SendAsync(new DeleteManufacturerCommand(Id));

        deleting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);

            return;
        }

        Navigation.NavigateTo("/dm/manufacturers");
    }
}
