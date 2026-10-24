namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOverview;

public interface IGetDeviceOverviewQueryRepository
{
    Task<DeviceOverview> GetOverviewAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
}
