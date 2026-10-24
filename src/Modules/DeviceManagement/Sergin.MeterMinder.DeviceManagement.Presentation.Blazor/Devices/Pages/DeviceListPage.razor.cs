using System.Globalization;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.Times;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Pages;

public sealed partial class DeviceListPage
{
    private SerginAggregateList<GetDeviceListItem> list = default!;
    private DeviceOverview? overview;

    // The filter panel's options, loaded the first time it opens.
    private IReadOnlyCollection<GetManufacturerListItem> manufacturers = [];
    private IReadOnlyCollection<GetDeviceModelListItem> models = [];
    private IReadOnlyList<ScopeOption> scopes = [];
    private TimeZoneInfo timeZone = TimeZoneInfo.Utc;
    private bool optionsLoaded;

    private Guid? filterManufacturerId;
    private Guid? filterModelId;
    private Guid? filterScopeId;
    private DateRange? filterCreated;

    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    private IReadOnlyList<SerginBreadcrumb> Trail { get; } = [SerginBreadcrumb.Of(DeviceManagementNavigation.Devices)];

    // A new device lands on the current scope, which is where CreateDevice checks Update.
    private IReadOnlyList<SerginPageAction> Actions { get; } =
    [
        new("New device", Icons.Material.Filled.Add, SerginActionKind.Primary, Href: "/dm/devices/new", Permission: DevicePermissions.Update),
    ];

    private IEnumerable<(string Label, string Value, string Sub)> OverviewCards =>
    [
        ("Devices", Count(overview?.Devices), "in this scope"),
        ("Models in use", Count(overview?.ModelsInUse), overview is null ? string.Empty : $"of {overview.ModelsTotal.ToString(Culture)}"),
        ("Manufacturers", Count(overview?.Manufacturers), "in the catalog"),
        ("Added · 30d", overview is null ? "–" : $"+{overview.AddedLast30Days.ToString(Culture)}", "last 30 days"),
    ];

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private IScopeDirectory ScopeDirectory { get; set; } = default!;

    [Inject]
    private IUiTimeZoneStore TimeZones { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadOverviewAsync();

    private static string Count(int? value) => value?.ToString(Culture) ?? "–";

    private async Task LoadOverviewAsync()
    {
        ErrorOr<DeviceOverview> result = await Dispatcher.SendAsync(new GetDeviceOverviewQueryCommand());

        // The overview is a summary; a failure leaves dashes rather than a second error beside the list's.
        overview = result.IsError ? null : result.Value;
    }

    private Task<ErrorOr<ListQueryResponse<GetDeviceListItem>>> LoadAsync(SerginListRequest request, CancellationToken cancellationToken) =>
        Dispatcher.SendAsync(
            new GetDeviceListQueryCommand(request.Paggination, request.Term, request.Filtering, request.Sorting), cancellationToken);

    private async Task ReloadAsync()
    {
        await LoadOverviewAsync();
        await list.ReloadAsync();
    }

    private async Task LoadFilterOptionsAsync()
    {
        if (optionsLoaded)
        {
            return;
        }

        // The same 200-row ceiling as CreateDevicePage's pickers; the catalog is tens of rows today.
        ErrorOr<ListQueryResponse<GetManufacturerListItem>> loaded =
            await Dispatcher.SendAsync(new GetManufacturerListQueryCommand(Paggination.Create(200, 1)));

        if (loaded.IsError)
        {
            ErrorPresenter.Notify(loaded.FirstError);
        }
        else
        {
            manufacturers = loaded.Value.Data;
        }

        scopes = await ScopeDirectory.GetReachableAsync();
        timeZone = await TimeZones.GetTimeZoneAsync();
        optionsLoaded = true;
    }

    private async Task OnFilterManufacturerChangedAsync(Guid? manufacturerId)
    {
        filterManufacturerId = manufacturerId;
        filterModelId = null;
        models = [];

        if (manufacturerId is not { } id)
        {
            return;
        }

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> loaded =
            await Dispatcher.SendAsync(new GetDeviceModelListQueryCommand(new ManufacturerId(id), Paggination.Create(200, 1)));

        if (loaded.IsError)
        {
            ErrorPresenter.Notify(loaded.FirstError);
            return;
        }

        models = loaded.Value.Data;
    }

    private IReadOnlyList<SerginAppliedFilter> BuildFilters()
    {
        List<SerginAppliedFilter> filters = [];

        if (filterManufacturerId is { } manufacturerId)
        {
            string name = manufacturers.FirstOrDefault(m => m.Id == manufacturerId)?.Name ?? "?";
            filters.Add(new(DeviceListFields.ManufacturerId, FilteringType.equals, manufacturerId.ToString(), $"Manufacturer: {name}"));
        }

        if (filterModelId is { } modelId)
        {
            string name = models.FirstOrDefault(m => m.Id == modelId)?.Name ?? "?";
            filters.Add(new(DeviceListFields.DeviceModelId, FilteringType.equals, modelId.ToString(), $"Model: {name}"));
        }

        if (filterScopeId is { } scopeId)
        {
            string name = scopes.FirstOrDefault(s => s.Id == scopeId)?.Name ?? "?";
            filters.Add(new(DeviceListFields.ScopeId, FilteringType.equals, scopeId.ToString(), $"Scope: {name}"));
        }

        if (filterCreated is { } created && (created.Start is not null || created.End is not null))
        {
            // Whole days in the viewer's zone: from the start of the first day to the start of the day after the last.
            string? from = created.Start is { } start ? ToUtcIso(start.Date) : null;
            string? to = created.End is { } end ? ToUtcIso(end.Date.AddDays(1)) : null;

            filters.Add(new(DeviceListFields.CreatedAtUtc, FilteringType.between, new[] { from, to }, $"Created: {DayLabel(created.Start)} – {DayLabel(created.End)}"));
        }

        return [.. filters];
    }

    private string ToUtcIso(DateTime localDay) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDay, DateTimeKind.Unspecified), timeZone).ToString("O", CultureInfo.InvariantCulture);

    private static string DayLabel(DateTime? day) => day?.ToString("dd MMM", Culture) ?? "…";

    private void OnFilterRemoved(SerginAppliedFilter filter)
    {
        switch (filter.Id)
        {
            case DeviceListFields.ManufacturerId:
                filterManufacturerId = null;
                filterModelId = null;
                models = [];
                break;
            case DeviceListFields.DeviceModelId:
                filterModelId = null;
                break;
            case DeviceListFields.ScopeId:
                filterScopeId = null;
                break;
            case DeviceListFields.CreatedAtUtc:
                filterCreated = null;
                break;
            default:
                break;
        }
    }

    private void ResetFilterFields()
    {
        filterManufacturerId = null;
        filterModelId = null;
        filterScopeId = null;
        filterCreated = null;
        models = [];
    }

    // The row command shares the detail page's confirm and its version rule: the delete is checked against the
    // version read just before it, and a stale answer reloads the list.
    private async Task DeleteAsync(GetDeviceListItem item)
    {
        if (!await DialogService.ConfirmDeleteAsync("Delete device", $"Delete {item.DeviceId}? It disappears from every list and page."))
        {
            return;
        }

        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(item.Id));

        if (loaded.IsError)
        {
            ErrorPresenter.Notify(loaded.FirstError);
            await ReloadAsync();
            return;
        }

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(item.Id), loaded.Value.Version);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);
        }

        await ReloadAsync();
    }
}
