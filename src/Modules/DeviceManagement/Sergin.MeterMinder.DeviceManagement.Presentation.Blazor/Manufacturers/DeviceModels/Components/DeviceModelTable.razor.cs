using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.Blazor.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Components;

/// <summary>
/// The models of one manufacturer: a paged table with a "New model" button, rendered inside
/// <c>ManufacturerDetailPage</c>. Not a page — it has no route, only the owning manufacturer's id.
/// </summary>
public sealed partial class DeviceModelTable
{
    [Parameter]
    [EditorRequired]
    public Guid ManufacturerId { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task<TableData<GetDeviceModelListItem>> LoadModelsAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceModelListQueryCommand(new ManufacturerId(ManufacturerId), Paggination.Create(state.PageSize, state.Page + 1)),
                cancellationToken);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.FirstError);

            return new TableData<GetDeviceModelListItem> { Items = [], TotalItems = 0 };
        }

        return new TableData<GetDeviceModelListItem> { Items = result.Value.Data, TotalItems = result.Value.Total };
    }

    private void OpenModel(GetDeviceModelListItem? item)
    {
        if (item is not null)
        {
            Navigation.NavigateTo($"/dm/manufacturers/{ManufacturerId}/models/{item.Id}");
        }
    }
}
