using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerListPage
{
    private SerginAggregateList<GetManufacturerListItem> list = default!;

    private IReadOnlyList<SerginBreadcrumb> Trail { get; } = [SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers)];

    // The manufacturer is not Scoped(), so the permission is checked on any scope, as the pipeline checks it.
    private IReadOnlyList<SerginPageAction> Actions { get; } =
    [
        new("New manufacturer", Icons.Material.Filled.Add, SerginActionKind.Primary, Href: "/dm/manufacturers/new",
            Permission: ManufacturerPermissions.Update, AnyScope: true),
    ];

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    private Task<ErrorOr<ListQueryResponse<GetManufacturerListItem>>> LoadAsync(SerginListRequest request, CancellationToken cancellationToken) =>
        Dispatcher.SendAsync(
            new GetManufacturerListQueryCommand(request.Paggination, request.Term, request.Filtering, request.Sorting), cancellationToken);

    private Task ReloadAsync() => list.ReloadAsync();

    // Same confirm and version rule as the detail page: checked against the version read just before the delete.
    private async Task DeleteAsync(GetManufacturerListItem item)
    {
        if (!await DialogService.ConfirmDeleteAsync(
                "Delete manufacturer", $"Delete {item.Name} and its models? They disappear from every list and page."))
        {
            return;
        }

        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(item.Id));

        if (loaded.IsError)
        {
            ErrorPresenter.Notify(loaded.FirstError);
            await list.ReloadAsync();
            return;
        }

        ErrorOr<Versioned<DeleteManufacturerCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteManufacturerCommand(item.Id), loaded.Value.Version);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);
        }

        await list.ReloadAsync();
    }
}
