using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.ReadProfile;

internal sealed class ReadDeviceProfileCommandHandler(IDeviceRepository repository)
    : ICommandHandler<ReadDeviceProfileCommand>
{
    internal static readonly TimeSpan SimulatedReadDuration = TimeSpan.FromSeconds(3);

    public async Task<ErrorOr<Success>> Handle(ReadDeviceProfileCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(request.Id, cancellationToken))
        {
            return Error.NotFound();
        }

        // The delay stands in for meter communication; nothing is read from a meter and nothing is written.
        await Task.Delay(SimulatedReadDuration, cancellationToken);

        return Result.Success;
    }
}
