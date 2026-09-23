using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Aggregates;

namespace Sergin.MeterMinder.DeviceManagement.Application.Devices;

internal sealed class DeviceAggregateConfiguration : IAggregateConfiguration<Device>
{
    public void Configure(AggregateFeatureBuilder<Device> builder) => builder.Audited();
}
