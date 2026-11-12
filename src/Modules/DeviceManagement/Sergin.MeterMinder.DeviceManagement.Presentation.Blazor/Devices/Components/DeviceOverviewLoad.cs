namespace Sergin.MeterMinder.DeviceManagement.Presentation.Blazor.Devices.Components;

/// <summary>The device overview both overview slots show.</summary>
internal static class DeviceOverviewLoad
{
    // The overview is a summary; a failure leaves dashes rather than a second error beside the list's.
    public static async Task<DeviceOverview?> LoadAsync(ISerginDispatcher dispatcher)
    {
        ErrorOr<DeviceOverview> result = await dispatcher.SendAsync(new GetDeviceOverviewQueryCommand());

        return result.IsError ? null : result.Value;
    }
}
