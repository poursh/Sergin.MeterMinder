using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetDeviceModelList;
using Sergin.MeterMinder.DeviceManagement.Application.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerDetailPage
{
    private ManufacturerQueryResponse? manufacturer;
    private SerginProblem? problem;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

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

    private async Task<TableData<GetDeviceModelListItem>> LoadModelsAsync(TableState state, CancellationToken cancellationToken)
    {
        // MudBlazor's TableState.Page is 0-based; Sergin's PageIndex is 1-based.
        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result =
            await Dispatcher.SendAsync(
                new GetDeviceModelListQueryCommand(new ManufacturerId(Id), Paggination.Create(state.PageSize, state.Page + 1)),
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
            Navigation.NavigateTo($"/dm/manufacturers/{Id}/models/{item.Id}");
        }
    }
}
