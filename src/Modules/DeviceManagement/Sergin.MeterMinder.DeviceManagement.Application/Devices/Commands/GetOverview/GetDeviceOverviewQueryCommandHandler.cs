using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Times;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.GetOverview;

internal sealed class GetDeviceOverviewQueryCommandHandler(IGetDeviceOverviewQueryRepository repository, IDateTimeProvider clock)
    : IQueryHandler<GetDeviceOverviewQueryCommand, DeviceOverview>
{
    // "Added" covers the last 30 days up to now, not calendar months.
    private static readonly TimeSpan AddedWindow = TimeSpan.FromDays(30);

    public async Task<ErrorOr<DeviceOverview>> Handle(GetDeviceOverviewQueryCommand request, CancellationToken cancellationToken) =>
        await repository.GetOverviewAsync(clock.UtcNow - AddedWindow, cancellationToken);
}
