using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetList;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerListPage
{
    private IReadOnlyList<SerginBreadcrumb> Trail { get; } = [SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers)];

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task<TableData<GetManufacturerListItem>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> result =
            await Dispatcher.SendAsync(
                new GetManufacturerListQueryCommand(Paggination.Create(state.PageSize, state.Page + 1)), cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetManufacturerListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetManufacturerListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenManufacturer(GetManufacturerListItem? item)
    {
        if (item is not null)
        {
            Navigation.NavigateTo($"/dm/manufacturers/{item.Id}");
        }
    }
}
