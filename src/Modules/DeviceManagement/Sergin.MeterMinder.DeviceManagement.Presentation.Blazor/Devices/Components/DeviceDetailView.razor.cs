using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Blazor.ScheduledTasks;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

public sealed partial class DeviceDetailView
{
    // The module's schema: where the device's schedules are stored.
    private const string Schema = "dm";

    private DeviceQueryResponse? device;
    private SerginProblem? problem;
    private bool deleting;
    private bool reading;
    private RowVersion? version;
    private SerginPageAction? schedule;
    private SerginScheduledTasksSection section = default!;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ISerginDispatcher Dispatcher { get; set; } = default!;

    [Inject]
    private IUiErrorPresenter ErrorPresenter { get; set; } = default!;

    [Inject]
    private IDialogService DialogService { get; set; } = default!;

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    private IScopeAuthorization ScopeAuthorization { get; set; } = default!;

    [CascadingParameter]
    private SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;

    // A property, not a field: the tail is the page title's word until the load fills in the device id, and
    // on the not-found path it stays that way beside the problem panel.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Devices),
        device is null ? new(DeviceTexts.Singular) : SerginBreadcrumb.Data(device.DeviceId),
    ];

    // Edit, Read profile and Schedule…, then Delete at the far end; each hidden from a user who lacks its permission on the
    // current scope. Schedule… is null for a user who may not manage scheduled tasks at all.
    private IReadOnlyList<SerginPageAction> Actions =>
    [
        new(CommonTexts.Edit, Icons.Material.Outlined.Edit, SerginActionKind.Secondary, Href: $"/dm/devices/{Id}/edit", Permission: DevicePermissions.Update),
        new(DeviceUiTexts.ReadProfile, Icons.Material.Outlined.Sensors, SerginActionKind.Secondary, OnClick: EventCallback.Factory.Create(this, ReadProfileAsync),
            Permission: DevicePermissions.ReadProfile, Disabled: reading),
        .. schedule is null ? Array.Empty<SerginPageAction>() : [schedule],
        new(CommonTexts.Delete, Icons.Material.Outlined.Delete, SerginActionKind.Danger, OnClick: EventCallback.Factory.Create(this, DeleteAsync),
            Permission: DevicePermissions.Delete, Disabled: deleting),
    ];

    protected override async Task OnInitializedAsync() =>
        schedule = await SerginScheduleAction.PageAsync(
            ScopeAuthorization, EventCallback.Factory.Create(this, ScheduleAsync), DevicePermissions.ReadProfile);

    protected override Task OnParametersSetAsync() => LoadAsync();

    private Task<ErrorOr<AuditInfo>> LoadAuditAsync() => Dispatcher.SendAsync(new GetDeviceAuditQueryCommand(new DeviceIntenralId(Id)));

    // Keeps the version the device was read at, so the delete can say which device it means.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded = await Dispatcher.SendVersionedAsync(new GetDeviceByIdQueryCommand(new DeviceIntenralId(Id)));

        if (loaded.IsError)
        {
            device = null;
            version = null;
            problem = ErrorPresenter.Present(loaded.FirstError);

            return;
        }

        problem = null;
        device = loaded.Value.Value;
        version = loaded.Value.Version;
    }

    // Sent directly, not through a one-off run: the button stays busy for the read's few seconds.
    private async Task ReadProfileAsync()
    {
        reading = true;

        try
        {
            ErrorOr<Success> result = await Dispatcher.SendAsync(new ReadDeviceProfileCommand(new DeviceIntenralId(Id)));

            if (result.IsError)
            {
                ErrorPresenter.Notify(result.Errors);
                return;
            }

            Snackbar.Add(Localizer.Text(DeviceUiTexts.ReadProfileDone, new { device?.DeviceId }), Severity.Success);
        }
        finally
        {
            reading = false;
        }
    }

    private async Task ScheduleAsync()
    {
        IReadOnlyCollection<ScheduledTaskTarget> targets = [new ScheduledTaskTarget(Id, null)];

        bool saved = await DialogService.ShowScheduleAsync(new SerginScheduleRequest(
            Schema,
            DeviceUiTexts.ReadProfile,
            () => new ReadDeviceProfileCommand(new DeviceIntenralId(Id)),
            targets,
            [device?.DeviceId ?? Id.ToString()]));

        if (saved)
        {
            await section.ReloadAsync();
        }
    }

    private async Task DeleteAsync()
    {
        if (!await DialogService.ConfirmDeleteAsync(
                Localizer, DeviceUiTexts.DeleteTitle, DeviceUiTexts.DeleteConfirm, new { device?.DeviceId }))
        {
            return;
        }

        deleting = true;

        ErrorOr<Versioned<DeleteDeviceCommandResponse>> result =
            await Dispatcher.SendVersionedAsync(new DeleteDeviceCommand(new DeviceIntenralId(Id)), version);

        deleting = false;

        if (result.IsError)
        {
            ErrorPresenter.Notify(result.Errors);

            // Someone changed the device after this view loaded it: show what is there now, with its version.
            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        await Host.LeaveAsync("/dm/devices");
    }
}
