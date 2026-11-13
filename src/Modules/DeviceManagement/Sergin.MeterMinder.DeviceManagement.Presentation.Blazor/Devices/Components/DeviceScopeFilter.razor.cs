using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>
/// The device list's scope filter: the scopes the caller's grants reach, as an indented list. A filter component of the
/// generated list (<c>Value</c>, <c>ValueChanged</c>), supplied because Scope has no list query to generate a picker from;
/// it names the chosen scope for the filter's chip.
/// </summary>
public sealed partial class DeviceScopeFilter : IListFilterLabel
{
    private IReadOnlyList<ScopeOption> scopes = [];

    [Parameter]
    public Guid? Value { get; set; }

    [Parameter]
    public EventCallback<Guid?> ValueChanged { get; set; }

    [Inject]
    private IStringLocalizer Localizer { get; set; } = default!;

    [Inject]
    private IScopeDirectory ScopeDirectory { get; set; } = default!;

    public string? LabelOf(Guid value) => scopes.FirstOrDefault(scope => scope.Id == value)?.Name;

    protected override async Task OnInitializedAsync() => scopes = await ScopeDirectory.GetReachableAsync();
}
