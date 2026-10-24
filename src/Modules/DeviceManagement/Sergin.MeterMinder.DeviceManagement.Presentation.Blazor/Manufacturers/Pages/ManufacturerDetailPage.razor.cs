using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Components;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.Pages;

public sealed partial class ManufacturerDetailPage
{
    private ManufacturerQueryResponse? manufacturer;
    private ManufacturerOverview? overview;
    private DeviceModelSection? modelSection;
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

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    // The manufacturer is not Scoped(), so its actions are checked on any scope, as the pipeline checks them.
    private IReadOnlyList<SerginPageAction> Actions =>
    [
        new("Edit", Icons.Material.Outlined.Edit, SerginActionKind.Secondary, Href: $"/dm/manufacturers/{Id}/edit",
            Permission: ManufacturerPermissions.Update, AnyScope: true),
        new("Delete", Icons.Material.Outlined.Delete, SerginActionKind.Danger, OnClick: EventCallback.Factory.Create(this, DeleteAsync),
            Permission: ManufacturerPermissions.Delete, AnyScope: true, Disabled: deleting),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    private Task<ErrorOr<AuditInfo>> LoadAuditAsync() => Dispatcher.SendAsync(new GetManufacturerAuditQueryCommand(Id));

    private async Task ReloadAsync()
    {
        await LoadAsync();

        if (modelSection is not null)
        {
            await modelSection.ReloadAsync();
        }
    }

    // A model command moved the manufacturer's version (or found it stale): read both again.
    private Task OnModelsChangedAsync() => LoadAsync();

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

        ErrorOr<ManufacturerOverview> figures = await Dispatcher.SendAsync(new GetManufacturerOverviewQueryCommand(Id));
        overview = figures.IsError ? null : figures.Value;
    }

    private async Task DeleteAsync()
    {
        if (!await DialogService.ConfirmDeleteAsync(
                "Delete manufacturer", $"Delete {manufacturer?.Name} and its models? They disappear from every list and page."))
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
