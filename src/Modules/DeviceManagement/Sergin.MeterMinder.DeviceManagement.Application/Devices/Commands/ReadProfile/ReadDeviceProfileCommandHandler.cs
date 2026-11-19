using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.ScheduledTasks;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices.Commands.ReadProfile;

internal sealed class ReadDeviceProfileCommandHandler(IDeviceRepository repository, IRunProgress progress)
    : ICommandHandler<ReadDeviceProfileCommand>
{
    /// <summary>One simulated step of the read; the read takes <see cref="StepProgress"/>'s length of them.</summary>
    internal static readonly TimeSpan SimulatedStepDuration = TimeSpan.FromSeconds(1);

    /// <summary>The progress reported after each step, in percent.</summary>
    private static readonly int[] StepProgress = [33, 66, 100];

    public async Task<ErrorOr<Success>> Handle(ReadDeviceProfileCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ExistsAsync(request.Id, cancellationToken))
        {
            return Error.NotFound();
        }

        // The delays stand in for meter communication; nothing is read from a meter and nothing is written.
        foreach (int percent in StepProgress)
        {
            await Task.Delay(SimulatedStepDuration, cancellationToken);
            await progress.ReportAsync(percent, cancellationToken);
        }

        return Result.Success;
    }
}
