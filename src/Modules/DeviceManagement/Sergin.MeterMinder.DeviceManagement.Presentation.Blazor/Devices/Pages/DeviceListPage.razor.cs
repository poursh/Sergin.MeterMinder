using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetList;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceListPage
{
    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task<TableData<GetDeviceListItem>> LoadAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetDeviceListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceListQueryCommand(Paggination.Create(state.PageSize, state.Page + 1)), cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetDeviceListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetDeviceListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenDevice(GetDeviceListItem? item)
    {
        if (item is not null)
        {
            Navigation.NavigateTo($"/dm/devices/{item.Id}");
        }
    }
}
