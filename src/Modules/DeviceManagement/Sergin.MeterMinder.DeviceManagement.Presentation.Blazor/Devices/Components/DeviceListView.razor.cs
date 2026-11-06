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
using Sergin.SharedKernel.Presentation.Localizations;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

public sealed partial class DeviceListView
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

    private IReadOnlyList<SerginBreadcrumb> Trail { get; } = [SerginBreadcrumb.Of(DeviceManagementNavigation.Devices)];

    // A new device lands on the current scope, which is where CreateDevice checks Update.
    private IReadOnlyList<SerginPageAction> Actions { get; } =
    [
        new(DeviceTexts.New, Icons.Material.Filled.Add, SerginActionKind.Primary, Href: "/dm/devices/new", Permission: DevicePermissions.Update),
    ];

    // The selection bar's Delete: one confirm for every selected device, each then sent as the row command sends it.
    private IReadOnlyList<SerginBulkAction<GetDeviceListItem>> BulkActions =>
    [
        new(CommonTexts.Delete, Icons.Material.Outlined.Delete, SerginActionKind.Danger, DeleteSelectedAsync, Permission: DevicePermissions.Delete),
    ];

    private IEnumerable<(TextDefinition Label, string Value, string Sub)> OverviewCards =>
    [
        (DeviceTexts.OverviewDevices, Count(overview?.Devices), Localizer.Text(DeviceTexts.OverviewInScope)),
        (DeviceTexts.OverviewModelsInUse, Count(overview?.ModelsInUse),
            overview is null ? string.Empty : Localizer.Text(DeviceTexts.OverviewOfTotal, new { Total = Formatter.Number(overview.ModelsTotal) })),
        (DeviceTexts.OverviewManufacturers, Count(overview?.Manufacturers), Localizer.Text(DeviceTexts.OverviewInCatalog)),
        (DeviceTexts.OverviewAdded,
            overview is null ? "–" : Localizer.Text(DeviceTexts.OverviewAddedCount, new { Count = Formatter.Number(overview.AddedLast30Days) }),
            Localizer.Text(DeviceTexts.OverviewLastMonth)),
    ];

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ICultureFormatter Formatter { get; set; } = default!;

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IScopeDirectory ScopeDirectory { get; set; } = default!;

    [Inject]
    private IUiTimeZoneStore TimeZones { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadOverviewAsync();

    private string Count(int? value) => value is { } number ? Formatter.Number(number) : "–";

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

        // The same 200-row ceiling as DeviceCreatePage's pickers; the catalog is tens of rows today.
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
            filters.Add(new(DeviceListFields.ManufacturerId, FilteringType.equals, manufacturerId.ToString(), Chip(DeviceTexts.ManufacturerId, name)));
        }

        if (filterModelId is { } modelId)
        {
            string name = models.FirstOrDefault(m => m.Id == modelId)?.Name ?? "?";
            filters.Add(new(DeviceListFields.DeviceModelId, FilteringType.equals, modelId.ToString(), Chip(DeviceTexts.DeviceModelId, name)));
        }

        if (filterScopeId is { } scopeId)
        {
            string name = scopes.FirstOrDefault(s => s.Id == scopeId)?.Name ?? "?";
            filters.Add(new(DeviceListFields.ScopeId, FilteringType.equals, scopeId.ToString(), Chip(DeviceTexts.ScopeId, name)));
        }

        if (filterCreated is { } created && (created.Start is not null || created.End is not null))
        {
            // Whole days in the viewer's zone: from the start of the first day to the start of the day after the last.
            string? from = created.Start is { } start ? ToUtcIso(start.Date) : null;
            string? to = created.End is { } end ? ToUtcIso(end.Date.AddDays(1)) : null;

            string range = Localizer.Text(DeviceTexts.DateRange, new { From = DayLabel(created.Start), To = DayLabel(created.End) });

            filters.Add(new(DeviceListFields.CreatedAtUtc, FilteringType.between, new[] { from, to }, Chip(DeviceTexts.CreatedAtUtc, range)));
        }

        return [.. filters];
    }

    // A chip names its filter by the label of the list field's own field text (spec decision 7), then the value.
    private string Chip(FieldText field, string value) =>
        Localizer.Text(DeviceTexts.FilterChip, new { Field = Localizer.Text(field.Label), Value = value });

    private string ToUtcIso(DateTime localDay) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDay, DateTimeKind.Unspecified), timeZone).ToString("O", CultureInfo.InvariantCulture);

    // The picked day is a local calendar day, so it is shown as it is, without a zone shift.
    private string DayLabel(DateTime? day) => day is { } value ? Formatter.Date(new DateTimeOffset(value.Date, TimeSpan.Zero)) : "…";

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
    // version read just before it, and a stale answer reloads the list. The bulk delete sends each device the same way.
    private async Task DeleteAsync(GetDeviceListItem item)
    {
        if (!await DialogService.ConfirmDeleteAsync(Localizer, DeviceTexts.DeleteTitle, DeviceTexts.DeleteConfirm, new { item.DeviceId }))
        {
            return;
        }

        ErrorOr<Success> result = await SendDeleteAsync(item);

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);
        }

        await ReloadAsync();
    }

    private async Task DeleteSelectedAsync(IReadOnlyList<GetDeviceListItem> items)
    {
        if (!await DialogService.ConfirmDeleteAsync(
                Localizer, DeviceTexts.DeleteManyTitle, DeviceTexts.DeleteManyConfirm, new { Count = Formatter.Number(items.Count) }))
        {
            return;
        }

        SerginBulkOutcome outcome = await SerginBulk.RunAsync(items, SendDeleteAsync);

        outcome.Report(ErrorPresenter, Snackbar, Localizer, Formatter, CommonTexts.BulkDeleted);
        await ReloadAsync();
    }

    private async Task<ErrorOr<Success>> SendDeleteAsync(GetDeviceListItem item)
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(item.Id));

        if (loaded.IsError)
        {
            return loaded.Errors;
        }

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(item.Id), loaded.Value.Version);

        return result.IsError ? result.Errors : Result.Success;
    }
}
