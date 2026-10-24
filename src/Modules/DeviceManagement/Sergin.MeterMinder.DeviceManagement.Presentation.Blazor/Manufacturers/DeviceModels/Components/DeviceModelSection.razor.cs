using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Components;

/// <summary>
/// The models of one manufacturer, as the entity list section of <c>ManufacturerDetailPage</c>: a paged list with
/// each model's device count, an Add model action, and per-row Rename and Remove. Not a page — it has no route, only the
/// owning manufacturer's id.
/// </summary>
/// <remarks>
/// Removing a model changes the <c>Manufacturer</c> aggregate, so it is sent at the manufacturer's version, which
/// the page holds and passes in as <see cref="Version"/>. Any answer but a refusal changes that version, so the
/// component tells the page through <see cref="OnChanged"/> to reload it. Remove is disabled for a model in use —
/// the validator refuses it anyway, in every scope, which is why the count is too.
/// </remarks>
public sealed partial class DeviceModelSection
{
    private SerginEntityList<GetDeviceModelListItem> list = default!;
    private int? total;

    [Parameter]
    [EditorRequired]
    public Guid ManufacturerId { get; set; }

    /// <summary>The manufacturer's version, as the page last read it; null until it has one.</summary>
    [Parameter]
    public RowVersion? Version { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    // Adding a model changes the manufacturer, so it needs the same permission as editing it, on any scope:
    // the catalog belongs to no scope.
    private IReadOnlyList<SerginPageAction> Actions =>
    [
        new("Add model", Icons.Material.Filled.Add, SerginActionKind.Primary,
            Href: $"/dm/manufacturers/{ManufacturerId}/models/new",
            Permission: ManufacturerPermissions.Update, AnyScope: true),
    ];

    public Task ReloadAsync() => list.ReloadAsync();

    // The search reads the model name and the Name header sorts by it (DeviceModelListFields); the list has no filters.
    private Task<ErrorOr<ListQueryResponse<GetDeviceModelListItem>>> LoadModelsAsync(SerginListRequest request, CancellationToken cancellationToken) =>
        Dispatcher.SendAsync(
            new GetDeviceModelListQueryCommand(new ManufacturerId(ManufacturerId), request.Paggination, request.Term, sorting: request.Sorting),
            cancellationToken);

    private void OnTotalChanged(int count) => total = count;

    private async Task RemoveAsync(GetDeviceModelListItem model)
    {
        if (!await DialogService.ConfirmDeleteAsync(
                "Remove device model", $"Remove {model.Name}? It disappears from its manufacturer and every list.", "Remove"))
        {
            return;
        }

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> result = await Dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(new ManufacturerId(ManufacturerId), model.Id), Version);

        if (result.IsError)
        {
            // An in-use model arrives as one validation error; a stale manufacturer as a version error.
            ErrorPresenter.Notify(result.Errors);

            if (!result.Errors.Exists(VersionErrors.IsStale))
            {
                return;
            }
        }

        await OnChanged.InvokeAsync();
        await list.ReloadAsync();
    }
}
