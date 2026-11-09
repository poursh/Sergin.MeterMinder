using Microsoft.AspNetCore.Components;
using MudBlazor;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Presentation.Blazor.Errors;
using Sergin.SharedKernel.Presentation.Blazor.Pages;
using Sergin.SharedKernel.Presentation.Errors;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Manufacturers.DeviceModels.Components;

public sealed partial class DeviceModelDetailView
{
    private DeviceModelQueryResponse? deviceModel;
    private SerginProblem? problem;
    private bool removing;

    // The manufacturer's version: removing a model changes the Manufacturer aggregate, which is what is guarded.
    private RowVersion? manufacturerVersion;

    [Parameter]
    public Guid ManufacturerId { get; set; }

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

    [CascadingParameter]
    private SerginViewHost Host { get; set; } = SerginViewHost.Unhosted;

    // A property, not a field: both tail steps are the page title's words until the load fills them in, and
    // on the not-found path they stay that way beside the problem panel. The manufacturer step links by the
    // route parameter, not the read model's id, so it is a link before the load too.
    private IReadOnlyList<SerginBreadcrumb> Trail =>
    [
        SerginBreadcrumb.Of(DeviceManagementNavigation.Manufacturers),
        deviceModel is null
            ? new(ManufacturerTexts.Singular, $"/dm/manufacturers/{ManufacturerId}")
            : SerginBreadcrumb.Data(deviceModel.ManufacturerName, $"/dm/manufacturers/{ManufacturerId}"),
        deviceModel is null ? new(DeviceModelTexts.Singular) : SerginBreadcrumb.Data(deviceModel.Name),
    ];

    // Renaming and removing change the manufacturer, so they need its Update permission, on any scope like the
    // model list's commands: the catalog belongs to no scope.
    private IReadOnlyList<SerginPageAction> Actions =>
    [
        new(CommonTexts.Rename, Icons.Material.Outlined.Edit, SerginActionKind.Secondary,
            Href: $"/dm/manufacturers/{ManufacturerId}/models/{Id}/edit",
            Permission: ManufacturerPermissions.Update, AnyScope: true),
        new(CommonTexts.Remove, Icons.Material.Outlined.Delete, SerginActionKind.Danger, OnClick: EventCallback.Factory.Create(this, RemoveAsync),
            Permission: ManufacturerPermissions.Update, AnyScope: true, Disabled: removing),
    ];

    protected override Task OnParametersSetAsync() => LoadAsync();

    // The manufacturer's version is read before the model: a change to the model between the two reads then makes
    // the version stale, so Remove is refused instead of removing a model this view shows under an old name.
    private async Task LoadAsync()
    {
        ErrorOr<Versioned<ManufacturerQueryResponse>> manufacturer =
            await Dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(new ManufacturerId(ManufacturerId)));
        ErrorOr<DeviceModelQueryResponse> result =
            await Dispatcher.SendAsync(new GetDeviceModelByIdQueryCommand(new ManufacturerId(ManufacturerId), new DeviceModelInternalId(Id)));

        if (result.IsError)
        {
            deviceModel = null;
            manufacturerVersion = null;
            problem = ErrorPresenter.Present(result.FirstError);

            return;
        }

        problem = null;
        deviceModel = result.Value;

        // A failed read leaves no version; RemoveAsync then reports it instead of sending a fabricated one.
        manufacturerVersion = manufacturer.IsError ? null : manufacturer.Value.Version;
    }

    private async Task RemoveAsync()
    {
        if (!await DialogService.ConfirmDeleteAsync(
                Localizer, DeviceModelUiTexts.RemoveTitle, DeviceModelUiTexts.RemoveConfirm, new { deviceModel?.Name },
                CommonTexts.Remove))
        {
            return;
        }

        if (manufacturerVersion is not { } expectedVersion)
        {
            ErrorPresenter.Notify(Error.NotFound());
            await LoadAsync();

            return;
        }

        removing = true;

        ErrorOr<Versioned<RemoveDeviceModelCommandResponse>> result = await Dispatcher.SendVersionedAsync(
            new RemoveDeviceModelCommand(new ManufacturerId(ManufacturerId), new DeviceModelInternalId(Id)), expectedVersion);

        removing = false;

        if (result.IsError)
        {
            // An in-use model arrives as one validation error; a stale manufacturer as a version error.
            ErrorPresenter.Notify(result.Errors);

            if (result.Errors.Exists(VersionErrors.IsStale))
            {
                await LoadAsync();
            }

            return;
        }

        await Host.LeaveAsync($"/dm/manufacturers/{ManufacturerId}");
    }
}
